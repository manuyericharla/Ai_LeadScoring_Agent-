using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace LeadScoring.Api.Services;

public class BatchProcessingService(
    IBatchRepository batchRepository,
    IEmailService emailService,
    TokenService tokenService,
    IConfiguration configuration,
    ILogger<BatchProcessingService> logger,
    IServiceScopeFactory scopeFactory,
    ManualBatchProgressStore progressStore) : IBatchProcessingService
{
    public async Task ProcessActiveConfigsAsync(CancellationToken cancellationToken)
    {
        var runDateUtc = DateTime.UtcNow;
        if (await batchRepository.HasBatchRunOnDateAsync(runDateUtc, cancellationToken))
        {
            logger.LogInformation("Daily batch already executed for {Date}.", runDateUtc.Date);
            return;
        }

        var batchType = await GetNextBatchTypeAsync(cancellationToken);
        var leads = await GetLeadsForBatchTypeAsync(batchType, runDateUtc, cancellationToken);
        var cycleStartUtc = runDateUtc.Date.AddDays(-3);
        var marker = $"DailySequence_{batchType}_Sent";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var stageCounts = new int[5];
        var successTemplateContexts = new ConcurrentBag<BatchLeadTemplateContext>();

        foreach (var lead in leads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await batchRepository.HasBatchMarkerEventAsync(lead.Id, marker, cycleStartUtc, cancellationToken))
            {
                continue;
            }

            if (batchType == CampaignBatchType.Day4 && lead.LastEmailSentDateUtc.HasValue)
            {
                var hasEngagement = await batchRepository.HasEngagementSinceLastEmailAsync(
                    lead.Id,
                    lead.LastEmailSentDateUtc.Value,
                    cancellationToken);
                if (hasEngagement)
                {
                    continue;
                }
            }

            processed++;
            stageCounts[MapStage(lead.Stage)]++;

            var sendResult = await ProcessLeadForBatchAsync(lead, batchType, marker, cancellationToken, successTemplateContexts);
            if (sendResult)
            {
                success++;
            }
            else
            {
                failed++;
            }
        }

        await batchRepository.CreateBatchLogAsync(new BatchLog
        {
            RunDate = runDateUtc,
            BatchType = batchType,
            TotalLeadsProcessed = processed,
            SuccessCount = success,
            FailureCount = failed
        }, cancellationToken);

        await SendAdminSummaryAsync(
            runDateUtc,
            batchType,
            processed,
            success,
            failed,
            stageCounts,
            successTemplateContexts,
            cancellationToken);
    }

    public async Task<BatchPreviewResultDto> PreviewAsync(CampaignBatchType batchType, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        // Sequential: eligible query and aggregates share one scoped DbContext.
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken).ConfigureAwait(false);
        var agg = await batchRepository.GetLeadAggregatesForPreviewAsync(nowUtc, cancellationToken).ConfigureAwait(false);

        return new BatchPreviewResultDto(
            batchType,
            agg.TotalLeads,
            agg.Stage0Count,
            agg.Stage1Count,
            agg.Stage2Count,
            agg.Stage3Count,
            agg.Stage4Count,
            agg.NewLeadsCount,
            agg.Last2DaysInactiveCount,
            agg.Last4DaysSinceLastEmailCount,
            agg.DidNotOpenEmailCount,
            eligibleLeads.Count);
    }

    public async Task<BatchManualRunResultDto> RunManualAsync(CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var filtered = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, scope, nowUtc, cancellationToken);
        var leads = ApplyManualMaxLeads(filtered, maxLeads);
        var marker = $"DailySequence_{batchType}_Manual_{nowUtc:yyyyMMddHHmmss}";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var failures = new ConcurrentBag<BatchFailureInfoDto>();
        var stageCounts = new int[5];
        var successTemplateContexts = new ConcurrentBag<BatchLeadTemplateContext>();
        var maxParallelism = Math.Clamp(configuration.GetValue<int?>("BatchProcessing:ManualMaxParallelism") ?? 5, 1, 20);

        await Parallel.ForEachAsync(
            leads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (lead, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            Interlocked.Increment(ref processed);
            Interlocked.Increment(ref stageCounts[MapStage(lead.Stage)]);

            var sent = await ProcessLeadInIsolatedScopeAsync(lead.Id, batchType, marker, ct, successTemplateContexts);
            if (sent)
            {
                Interlocked.Increment(ref success);
            }
            else
            {
                Interlocked.Increment(ref failed);
                failures.Add(new BatchFailureInfoDto(lead.Id, lead.Email, "Max retries exceeded or template missing."));
            }
        });

        await batchRepository.CreateBatchLogAsync(new BatchLog
        {
            RunDate = nowUtc,
            BatchType = batchType,
            TotalLeadsProcessed = processed,
            SuccessCount = success,
            FailureCount = failed
        }, cancellationToken);

        await SendAdminSummaryAsync(nowUtc, batchType, processed, success, failed, stageCounts, successTemplateContexts, cancellationToken);

        return new BatchManualRunResultDto(
            batchType,
            processed,
            success,
            failed,
            failures.ToList());
    }

    public async Task<BatchManualRunStartDto> StartManualAsync(CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "TotalEligible" : scope.Trim();
        var normalizedMaxLeads = NormalizeManualMaxLeads(maxLeads);
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var filtered = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, normalizedScope, nowUtc, cancellationToken);
        var leads = ApplyManualMaxLeads(filtered, normalizedMaxLeads);
        var state = progressStore.CreateJob(batchType, normalizedScope, leads.Count);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                var result = await scopedService.RunManualTrackedAsync(state.JobId, batchType, normalizedScope, normalizedMaxLeads, CancellationToken.None);
                progressStore.Complete(state.JobId, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Manual batch background job failed for {BatchType}. JobId={JobId}", batchType, state.JobId);
                progressStore.Fail(state.JobId);
            }
        });

        return new BatchManualRunStartDto(state.JobId, batchType, normalizedScope);
    }

    public BatchManualRunStatusDto? GetManualStatus(Guid jobId) => progressStore.GetStatus(jobId);

    public async Task<IReadOnlyList<BatchLogHistoryDto>> GetBatchLogHistoryAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await batchRepository.GetRecentBatchLogsAsync(take, cancellationToken).ConfigureAwait(false);
        var list = new List<BatchLogHistoryDto>(rows.Count);
        foreach (var x in rows)
        {
            list.Add(new BatchLogHistoryDto(
                x.BatchId,
                x.RunDate,
                x.BatchType,
                x.TotalLeadsProcessed,
                x.SuccessCount,
                x.FailureCount));
        }

        return list;
    }

    public async Task<BatchManualRunResultDto> RunManualTrackedAsync(Guid jobId, CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var filtered = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, scope, nowUtc, cancellationToken);
        var leads = ApplyManualMaxLeads(filtered, maxLeads);
        var marker = $"DailySequence_{batchType}_Manual_{nowUtc:yyyyMMddHHmmss}";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var failures = new ConcurrentBag<BatchFailureInfoDto>();
        var stageCounts = new int[5];
        var successTemplateContexts = new ConcurrentBag<BatchLeadTemplateContext>();
        var maxParallelism = Math.Clamp(configuration.GetValue<int?>("BatchProcessing:ManualMaxParallelism") ?? 5, 1, 20);

        await Parallel.ForEachAsync(
            leads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (lead, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                Interlocked.Increment(ref processed);
                Interlocked.Increment(ref stageCounts[MapStage(lead.Stage)]);

                var sent = await ProcessLeadInIsolatedScopeAsync(lead.Id, batchType, marker, ct, successTemplateContexts);
                if (sent)
                {
                    Interlocked.Increment(ref success);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                    failures.Add(new BatchFailureInfoDto(lead.Id, lead.Email, "Max retries exceeded or template missing."));
                }

                progressStore.IncrementProcessed(jobId, sent);
            });

        await batchRepository.CreateBatchLogAsync(new BatchLog
        {
            RunDate = nowUtc,
            BatchType = batchType,
            TotalLeadsProcessed = processed,
            SuccessCount = success,
            FailureCount = failed
        }, cancellationToken);

        await SendAdminSummaryAsync(nowUtc, batchType, processed, success, failed, stageCounts, successTemplateContexts, cancellationToken);

        return new BatchManualRunResultDto(
            batchType,
            processed,
            success,
            failed,
            failures.ToList());
    }

    private async Task<bool> ProcessLeadInIsolatedScopeAsync(
        Guid leadId,
        CampaignBatchType batchType,
        string marker,
        CancellationToken cancellationToken,
        ConcurrentBag<BatchLeadTemplateContext>? successTemplateContexts = null)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();

        var lead = await scopedRepository.GetLeadForUpdateAsync(leadId, cancellationToken);
        if (lead is null)
        {
            return false;
        }

        return await ProcessLeadForBatchAsync(scopedRepository, lead, batchType, marker, cancellationToken, successTemplateContexts);
    }

    private async Task<List<Lead>> FilterManualLeadsByScopeAsync(
        List<Lead> eligibleLeads,
        List<Lead> allLeads,
        string? scope,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("TotalEligible", StringComparison.OrdinalIgnoreCase))
        {
            return eligibleLeads;
        }

        if (scope.Equals("TotalLeads", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads;
        }

        if (scope.Equals("Stage0", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Cold).ToList();
        }

        if (scope.Equals("Stage1", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Warm).ToList();
        }

        if (scope.Equals("Stage2", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Mql).ToList();
        }

        if (scope.Equals("Stage3", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Hot).ToList();
        }

        if (scope.Equals("Stage4", StringComparison.OrdinalIgnoreCase))
        {
            var known = new HashSet<LeadStage> { LeadStage.Cold, LeadStage.Warm, LeadStage.Mql, LeadStage.Hot };
            return allLeads.Where(x => !known.Contains(x.Stage)).ToList();
        }

        if (scope.Equals("NewLeads", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.CreatedAtUtc >= nowUtc.AddDays(-1)).ToList();
        }

        if (scope.Equals("Last2DaysInactive", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.LastActivityUtc <= nowUtc.AddDays(-2)).ToList();
        }

        if (scope.Equals("Last4DaysSinceEmail", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.LastEmailSentDateUtc.HasValue && x.LastEmailSentDateUtc <= nowUtc.AddDays(-4)).ToList();
        }

        if (scope.Equals("DidNotOpenEmail", StringComparison.OrdinalIgnoreCase))
        {
            return await batchRepository.GetLeadsDidNotOpenSinceLastEmailAsync(cancellationToken).ConfigureAwait(false);
        }

        return eligibleLeads;
    }

    private static int? NormalizeManualMaxLeads(int? maxLeads)
    {
        if (maxLeads is null || maxLeads.Value < 1)
        {
            return null;
        }

        return maxLeads.Value;
    }

    private static List<Lead> ApplyManualMaxLeads(List<Lead> leads, int? maxLeads)
    {
        if (maxLeads is null || maxLeads.Value < 1)
        {
            return leads;
        }

        var take = Math.Min(maxLeads.Value, leads.Count);
        return leads.Take(take).ToList();
    }

    public async Task<BatchRetryResultDto> RetryFailedLeadsAsync(long sourceBatchId, CancellationToken cancellationToken)
    {
        var sourceBatch = await batchRepository.GetBatchByIdAsync(sourceBatchId, cancellationToken);
        if (sourceBatch is null)
        {
            throw new InvalidOperationException($"Batch {sourceBatchId} not found.");
        }

        var failedLeads = await batchRepository.GetFailedBatchLeadsAsync(sourceBatchId, cancellationToken);
        if (failedLeads.Count == 0)
        {
            throw new InvalidOperationException($"Batch {sourceBatchId} has no failed leads to retry.");
        }

        var retryBatch = new Batch
        {
            ProductId = sourceBatch.ProductId,
            BatchType = sourceBatch.BatchType,
            StartTime = DateTime.UtcNow,
            Status = BatchStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        await batchRepository.CreateBatchAsync(retryBatch, cancellationToken);

        var retryBatchLeads = failedLeads.Select(x => new BatchLead
        {
            BatchId = retryBatch.BatchId,
            LeadId = x.LeadId,
            Status = BatchLeadStatus.Pending,
            RetryCount = x.RetryCount + 1
        }).ToList();
        await batchRepository.CreateBatchLeadsAsync(retryBatchLeads, cancellationToken);

        var successCount = 0;
        var failedCount = 0;
        foreach (var batchLead in retryBatchLeads)
        {
            var lead = await batchRepository.GetLeadForUpdateAsync(batchLead.LeadId, cancellationToken);
            if (lead is null)
            {
                batchLead.Status = BatchLeadStatus.Failed;
                batchLead.ErrorMessage = "Lead not found.";
                failedCount++;
            }
            else
            {
                var result = await ProcessLeadForBatchAsync(lead, CampaignBatchType.Day3, "RetryBatchSent", cancellationToken);
                batchLead.Status = result ? BatchLeadStatus.Success : BatchLeadStatus.Failed;
                batchLead.ErrorMessage = result ? null : "Retry failed.";
                if (result)
                {
                    successCount++;
                }
                else
                {
                    failedCount++;
                }
            }

            batchLead.ProcessedAt = DateTime.UtcNow;
        }

        retryBatch.TotalLeads = retryBatchLeads.Count;
        retryBatch.SuccessCount = successCount;
        retryBatch.FailedCount = failedCount;
        retryBatch.Status = failedCount > 0 ? BatchStatus.Failed : BatchStatus.Completed;
        retryBatch.EndTime = DateTime.UtcNow;
        await batchRepository.SaveChangesAsync(cancellationToken);

        return new BatchRetryResultDto(
            sourceBatchId,
            retryBatch.BatchId,
            retryBatch.TotalLeads,
            retryBatch.SuccessCount,
            retryBatch.FailedCount,
            retryBatch.Status);
    }

    private async Task<bool> ProcessLeadForBatchAsync(Lead lead, CampaignBatchType batchType, string marker, CancellationToken cancellationToken, ConcurrentBag<BatchLeadTemplateContext>? successTemplateContexts = null)
        => await ProcessLeadForBatchAsync(batchRepository, lead, batchType, marker, cancellationToken, successTemplateContexts);

    private async Task<bool> ProcessLeadForBatchAsync(
        IBatchRepository repository,
        Lead lead,
        CampaignBatchType batchType,
        string marker,
        CancellationToken cancellationToken,
        ConcurrentBag<BatchLeadTemplateContext>? successTemplateContexts = null)
    {
        var stageUsedForTemplate = lead.Stage;
        var productUsedForTemplate = lead.ProductId;

        var template = await repository.GetTemplateByBatchTypeAsync(batchType, lead, cancellationToken);
        if (template is null)
        {
            return false;
        }

        var eventName = $"batch_{batchType.ToString().ToLowerInvariant()}";
        var resolvedBody = ComposeBatchEmailHtml(template, lead, batchType, useTrackedCta: true);

        var sent = await SendWithRetryAsync(lead.Email, template.Subject, resolvedBody, cancellationToken);
        if (!sent)
        {
            return false;
        }

        successTemplateContexts?.Add(new BatchLeadTemplateContext(stageUsedForTemplate, productUsedForTemplate));

        var sentAtUtc = DateTime.UtcNow;
        lead.LastEmailSentDateUtc = sentAtUtc;
        lead.LastActivityUtc = sentAtUtc;
        AdvanceStageAfterSuccessfulBatchSend(lead);
        if (batchType == CampaignBatchType.Day1)
        {
            lead.WelcomeEmailSent = true;
        }
        else if (batchType == CampaignBatchType.Day4)
        {
            lead.NextEmailSendDateUtc = sentAtUtc.AddDays(7);
        }

        await repository.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Type = EventType.WebsiteActivity,
            Source = EventSource.Email,
            TimestampUtc = sentAtUtc,
            MetadataJson = $$"""{"eventName":"{{eventName}}","batchType":"{{batchType}}","systemMarker":"{{marker}}"}"""
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> SendWithRetryAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var maxRetries = configuration.GetValue<int?>("BatchProcessing:EmailRetryCount") ?? 3;
        var delayMs = configuration.GetValue<int?>("BatchProcessing:EmailRetryDelayMs") ?? 1000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await emailService.SendAsync(to, subject, htmlBody);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Email send failed for {Email}. Attempt {Attempt}/{Max}.", to, attempt, maxRetries);
                if (attempt == maxRetries)
                {
                    break;
                }

                await Task.Delay(delayMs * attempt, cancellationToken);
            }
        }

        return false;
    }

    private async Task SendAdminSummaryAsync(
        DateTime runDateUtc,
        CampaignBatchType batchType,
        int totalProcessed,
        int successCount,
        int failureCount,
        IReadOnlyList<int> stageCounts,
        ConcurrentBag<BatchLeadTemplateContext> successTemplateContexts,
        CancellationToken cancellationToken)
    {
        var adminEmailConfig = configuration["BatchProcessing:AdminEmail"];
        var configuredRecipients = (adminEmailConfig ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var storedRecipients = await batchRepository.GetAdminReportEmailsAsync(cancellationToken);

        var adminRecipients = configuredRecipients
            .Concat(storedRecipients)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (adminRecipients.Count == 0)
        {
            logger.LogWarning("No admin recipients found in BatchProcessing:AdminEmail or AdminBatchReports table.");
            return;
        }

        foreach (var recipient in adminRecipients)
        {
            await batchRepository.UpsertAdminReportAsync(
                recipient,
                stageCounts[0],
                stageCounts[1],
                stageCounts[2],
                stageCounts[3],
                stageCounts[4],
                cancellationToken);
        }

        var subject = $"[Batch Summary] {FormatCampaignBatchTypeLabel(batchType)} — {runDateUtc.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}";
        var recipientSections = await BuildRecipientPreviewSectionsAsync(
            batchType,
            stageCounts,
            successTemplateContexts,
            cancellationToken).ConfigureAwait(false);
        var body = BuildBatchSummaryEmailHtml(
            runDateUtc,
            batchType,
            totalProcessed,
            successCount,
            failureCount,
            stageCounts,
            recipientSections);

        foreach (var recipient in adminRecipients)
        {
            try
            {
                await SendAdminEmailWithoutBccAsync(recipient, subject, body, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send admin batch summary email to {Recipient}.", recipient);
            }
        }
    }

    private async Task SendAdminEmailWithoutBccAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var smtpSection = configuration.GetSection("SMTP");
        var emailSection = configuration.GetSection("Email");
        var host = smtpSection["Host"] ?? throw new InvalidOperationException("SMTP:Host is missing.");
        var port = int.Parse(smtpSection["Port"] ?? throw new InvalidOperationException("SMTP:Port is missing."));
        var username = smtpSection["Username"] ?? throw new InvalidOperationException("SMTP:Username is missing.");
        var password = smtpSection["Password"] ?? throw new InvalidOperationException("SMTP:Password is missing.");
        var fromAddress = emailSection["FromAddress"] ?? throw new InvalidOperationException("Email:FromAddress is missing.");

        using var message = new MailMessage(fromAddress, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    private string ComposeBatchEmailHtml(EmailTemplate template, Lead lead, CampaignBatchType batchType, bool useTrackedCta)
    {
        var eventName = $"batch_{batchType.ToString().ToLowerInvariant()}";
        var resolvedBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);
        if (!string.IsNullOrWhiteSpace(template.CtaButtonText) && !string.IsNullOrWhiteSpace(template.CtaLink))
        {
            var resolvedLink = ResolveTemplate(template.CtaLink, lead, eventName, template.IsTrackingEnabled);
            var href = useTrackedCta ? BuildTrackedClickUrl(lead, resolvedLink) : resolvedLink;
            resolvedBody += $"""

                <p style="margin-top:20px;">
                  <a href="{href}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                </p>
                """;
        }

        return resolvedBody;
    }

    private async Task<string> BuildRecipientPreviewSectionsAsync(
        CampaignBatchType batchType,
        IReadOnlyList<int> stageCounts,
        ConcurrentBag<BatchLeadTemplateContext> successTemplateContexts,
        CancellationToken cancellationToken)
    {
        var previewContexts = ResolvePreviewTemplateContexts(successTemplateContexts, batchType, stageCounts);
        var seenTemplates = new HashSet<int>();
        var sections = new StringBuilder();

        foreach (var ctx in previewContexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampleLead = CreateSamplePreviewLead(ctx.StageUsedForTemplate, ctx.ProductId);
            var template = await batchRepository.GetTemplateByBatchTypeAsync(batchType, sampleLead, cancellationToken).ConfigureAwait(false);
            if (template is null || !seenTemplates.Add(template.TemplateId))
            {
                continue;
            }

            var bodyHtml = ComposeBatchEmailHtml(template, sampleLead, batchType, useTrackedCta: false);
            var fragment = ExtractEmailBodyInnerFragment(bodyHtml);
            var subjectText = WebUtility.HtmlEncode(template.Subject);
            var templateName = WebUtility.HtmlEncode(template.Name);
            var audienceHint = WebUtility.HtmlEncode(DescribePreviewAudience(batchType, ctx.StageUsedForTemplate, ctx.ProductId));

            sections.Append(CultureInfo.InvariantCulture, $"""

<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:20px;background-color:#0a3550;border-radius:6px;">
<tr>
<td width="8" bgcolor="#6af47c" style="width:8px;background-color:#6af47c;font-size:1px;line-height:1px;">&nbsp;</td>
<td style="padding:18px 20px;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0">
<tr>
<td style="vertical-align:top;width:142px;padding:0 16px 0 0;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
<span style="display:block;font-size:11px;font-weight:700;letter-spacing:0.1em;color:#6af47c;text-transform:uppercase;">Recipients</span>
<span style="display:block;margin-top:8px;font-size:13px;line-height:1.4;color:#ffffff;font-weight:600;">What they received</span>
<span style="display:block;margin-top:8px;font-size:12px;line-height:1.45;color:#9ec9da;">{audienceHint}</span>
</td>
<td style="vertical-align:top;border-left:1px solid #144a62;padding:0 0 0 16px;">
<span style="display:block;font-size:11px;color:#9ec9da;text-transform:uppercase;font-weight:600;">Template name</span>
<span style="display:block;margin-top:4px;font-size:14px;color:#d8f3ff;font-weight:600;">{templateName}</span>
<span style="display:block;margin-top:12px;font-size:11px;color:#9ec9da;text-transform:uppercase;font-weight:600;">Subject line</span>
<span style="display:block;margin-top:4px;font-size:15px;line-height:1.35;color:#ffffff;">{subjectText}</span>
<span style="display:block;margin-top:14px;font-size:11px;color:#9ec9da;text-transform:uppercase;font-weight:600;">Message body (same as sent)</span>
<span style="display:block;margin-top:6px;font-size:11px;line-height:1.45;color:#8db6c9;">Preview uses sample merge data. Live sends use tracked links per recipient where enabled.</span>
<div style="margin-top:12px;background-color:#ffffff;border-radius:6px;padding:14px;color:#022232;font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:15px;line-height:1.5;">
""");
            sections.Append(fragment);
            sections.Append("""
</div>
</td>
</tr>
</table>
</td>
</tr>
</table>
""");
        }

        return sections.Length == 0 ? string.Empty : sections.ToString();
    }

    private static List<BatchLeadTemplateContext> ResolvePreviewTemplateContexts(
        ConcurrentBag<BatchLeadTemplateContext> successTemplateContexts,
        CampaignBatchType batchType,
        IReadOnlyList<int> stageCounts)
    {
        var raw = successTemplateContexts.ToArray();
        if (raw.Length > 0)
        {
            return raw
                .Distinct()
                .OrderBy(x => (int)x.StageUsedForTemplate)
                .ThenBy(x => x.ProductId is null ? 0 : 1)
                .ThenBy(x => x.ProductId ?? 0)
                .ToList();
        }

        return FallbackPreviewTemplateContexts(batchType, stageCounts);
    }

    private static List<BatchLeadTemplateContext> FallbackPreviewTemplateContexts(
        CampaignBatchType batchType,
        IReadOnlyList<int> stageCounts)
    {
        switch (batchType)
        {
            case CampaignBatchType.Day1:
            case CampaignBatchType.Day2:
                return new List<BatchLeadTemplateContext> { new(LeadStage.Cold, null) };
            case CampaignBatchType.Day3:
                return new[]
                {
                    LeadStage.Cold,
                    LeadStage.Warm,
                    LeadStage.Mql,
                    LeadStage.Hot
                }.Where(s => stageCounts[MapStage(s)] > 0).Select(s => new BatchLeadTemplateContext(s, null)).ToList();
            case CampaignBatchType.Day4:
                return new[] { LeadStage.Mql, LeadStage.Hot }
                    .Where(s => stageCounts[MapStage(s)] > 0)
                    .Select(s => new BatchLeadTemplateContext(s, null))
                    .ToList();
            default:
                return new List<BatchLeadTemplateContext>();
        }
    }

    private static string ExtractEmailBodyInnerFragment(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html ?? string.Empty;
        }

        var openBody = Regex.Match(html, "(?is)<body[^>]*>");
        if (!openBody.Success)
        {
            return html.Trim();
        }

        var innerStart = openBody.Index + openBody.Length;
        var closeIdx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (closeIdx <= innerStart)
        {
            return html.Trim();
        }

        return html[innerStart..closeIdx].Trim();
    }

    private static Lead CreateSamplePreviewLead(LeadStage stage, int? productId) => new()
    {
        Id = Guid.Parse("00000000-0000-4000-8000-000000000001"),
        Email = "recipient@example.com",
        FirstName = "Jordan",
        LastName = "Sample",
        Stage = stage,
        ProductId = productId
    };

    private static string DescribePreviewAudience(CampaignBatchType batchType, LeadStage stage, int? productId)
    {
        var baseText = batchType switch
        {
            CampaignBatchType.Day1 or CampaignBatchType.Day2 => "Cold leads — same template as in this batch run.",
            CampaignBatchType.Day3 => stage switch
            {
                LeadStage.Cold => "Cold stage template (used for Cold leads on Day 3).",
                LeadStage.Warm => "Warm stage template (used for Warm leads on Day 3).",
                LeadStage.Mql => "MQL stage template (used for MQL leads on Day 3).",
                LeadStage.Hot => "Hot stage template (used for Hot leads on Day 3).",
                _ => "Stage-specific template."
            },
            CampaignBatchType.Day4 => stage switch
            {
                LeadStage.Mql => "Follow-up template for engaged MQL leads without recent activity.",
                LeadStage.Hot => "Follow-up template for engaged Hot leads without recent activity.",
                _ => "Follow-up template."
            },
            _ => $"Template selected for audience in {stage} stage."
        };

        if (productId is int pid)
        {
            return $"{baseText} Product ID {pid}.";
        }

        return baseText;
    }

    private async Task<CampaignBatchType> GetNextBatchTypeAsync(CancellationToken cancellationToken)
    {
        var last = await batchRepository.GetLastCompletedDailyBatchTypeAsync(cancellationToken);
        return last switch
        {
            null => CampaignBatchType.Day1,
            CampaignBatchType.Day1 => CampaignBatchType.Day2,
            CampaignBatchType.Day2 => CampaignBatchType.Day3,
            CampaignBatchType.Day3 => CampaignBatchType.Day4,
            _ => CampaignBatchType.Day1
        };
    }

    private async Task<List<Lead>> GetLeadsForBatchTypeAsync(CampaignBatchType batchType, DateTime runDateUtc, CancellationToken cancellationToken)
    {
        return batchType switch
        {
            CampaignBatchType.Day1 => await batchRepository.GetDay1LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Day2 => await batchRepository.GetDay2LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Day3 => await batchRepository.GetDay3LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Day4 => await batchRepository.GetDay4LeadsAsync(runDateUtc, cancellationToken),
            _ => []
        };
    }

    /// <summary>
    /// Advances only the category stage after successful batch send (Cold→Warm→Mql→Hot).
    /// Does not alter total score.
    /// </summary>
    private static void AdvanceStageAfterSuccessfulBatchSend(Lead lead)
    {
        var before = lead.Stage;
        switch (lead.Stage)
        {
            case LeadStage.Cold:
                lead.Stage = LeadStage.Warm;
                break;
            case LeadStage.Warm:
                lead.Stage = LeadStage.Mql;
                break;
            case LeadStage.Mql:
                lead.Stage = LeadStage.Hot;
                break;
            case LeadStage.Hot:
                break;
            default:
                return;
        }

        if (before != lead.Stage)
        {
            lead.LastScoredAtUtc = DateTime.UtcNow;
        }
    }

    private static string FormatCampaignBatchTypeLabel(CampaignBatchType batchType) => batchType switch
    {
        CampaignBatchType.Day1 => "Day 1",
        CampaignBatchType.Day2 => "Day 2",
        CampaignBatchType.Day3 => "Day 3",
        CampaignBatchType.Day4 => "Day 4",
        _ => batchType.ToString()
    };

    private static string BuildBatchSummaryEmailHtml(
        DateTime runDateUtc,
        CampaignBatchType batchType,
        int totalProcessed,
        int successCount,
        int failureCount,
        IReadOnlyList<int> stageCounts,
        string recipientPreviewSectionsHtml)
    {
        var batchLabel = WebUtility.HtmlEncode(FormatCampaignBatchTypeLabel(batchType));
        var executionReadable = WebUtility.HtmlEncode(
            runDateUtc.ToString("dddd, MMMM d, yyyy 'at' HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
        var executionIso = WebUtility.HtmlEncode(runDateUtc.ToString("O", CultureInfo.InvariantCulture));

        var successColor = "#6af47c";
        var failColor = failureCount > 0 ? "#ff8a8a" : "#9ec9da";

        var stageLabels = new[] { ("Cold", "Stage 0"), ("Warm", "Stage 1"), ("MQL", "Stage 2"), ("Hot", "Stage 3"), ("Other", "Stage 4") };
        var stageRows = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            var (name, indexLabel) = stageLabels[i];
            stageRows.Append(CultureInfo.InvariantCulture, $"""
              <tr>
                <td style="padding:10px 14px;border-bottom:1px solid #144a62;font-size:14px;color:#d8f3ff;">{WebUtility.HtmlEncode(name)} <span style="color:#9ec9da;font-size:12px;">({WebUtility.HtmlEncode(indexLabel)})</span></td>
                <td align="right" style="padding:10px 14px;border-bottom:1px solid #144a62;font-size:14px;font-weight:700;color:#ffffff;">{stageCounts[i]}</td>
              </tr>
              """);
        }

        return $"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width"/>
<title>Batch summary</title>
</head>
<body style="margin:0;padding:0;background-color:#021f33;">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#021f33;padding:24px 12px;">
<tr>
<td align="center">
<table role="presentation" width="600" cellspacing="0" cellpadding="0" bgcolor="#032844" style="max-width:600px;width:100%;background-color:#032844;border-radius:8px;">
<tr>
<td style="padding:28px 28px 16px;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
<div style="font-size:12px;font-weight:700;letter-spacing:0.14em;color:#6af47c;text-transform:uppercase;">HIPERBRAINS</div>
<h1 style="margin:14px 0 0;font-size:24px;line-height:1.25;color:#ffffff;font-weight:700;">Lead campaign batch summary</h1>
<p style="margin:12px 0 0;font-size:15px;line-height:1.55;color:#d8f3ff;">Results from your latest scheduled outreach batch.</p>
</td>
</tr>
<tr>
<td style="padding:0 28px;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" bgcolor="#0a3550" style="background-color:#0a3550;border-radius:6px;margin-bottom:8px;">
<tr>
<td style="padding:16px 18px;border-bottom:1px solid #144a62;">
<span style="display:block;font-size:11px;letter-spacing:0.06em;color:#9ec9da;text-transform:uppercase;font-weight:600;">Batch type</span>
<span style="display:block;margin-top:4px;font-size:16px;font-weight:700;color:#ffffff;">{batchLabel}</span>
</td>
</tr>
<tr>
<td style="padding:16px 18px;border-bottom:1px solid #144a62;">
<span style="display:block;font-size:11px;letter-spacing:0.06em;color:#9ec9da;text-transform:uppercase;font-weight:600;">Execution (UTC)</span>
<span style="display:block;margin-top:4px;font-size:16px;font-weight:600;color:#ffffff;">{executionReadable}</span>
<span style="display:block;margin-top:4px;font-size:12px;color:#9ec9da;">Technical: {executionIso}</span>
</td>
</tr>
<tr>
<td style="padding:16px 18px;font-size:0;">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0">
<tr>
<td width="33%" valign="top" style="padding:8px;font-size:14px;color:#d8f3ff;">
<span style="display:block;font-size:11px;color:#9ec9da;text-transform:uppercase;font-weight:600;">Processed</span>
<span style="display:block;margin-top:6px;font-size:22px;font-weight:700;color:#ffffff;">{totalProcessed}</span>
</td>
<td width="33%" valign="top" style="padding:8px;font-size:14px;color:#d8f3ff;">
<span style="display:block;font-size:11px;color:#9ec9da;text-transform:uppercase;font-weight:600;">Sent</span>
<span style="display:block;margin-top:6px;font-size:22px;font-weight:700;color:{successColor};">{successCount}</span>
</td>
<td width="33%" valign="top" style="padding:8px;font-size:14px;color:#d8f3ff;">
<span style="display:block;font-size:11px;color:#9ec9da;text-transform:uppercase;font-weight:600;">Failed</span>
<span style="display:block;margin-top:6px;font-size:22px;font-weight:700;color:{failColor};">{failureCount}</span>
</td>
</tr>
</table>
</td>
</tr>
</table>
</td>
</tr>
<tr>
<td style="padding:8px 28px 8px;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
<h2 style="margin:0;font-size:15px;color:#ffffff;font-weight:700;">Leads by stage</h2>
<p style="margin:6px 0 0;font-size:13px;color:#9ec9da;line-height:1.4;">Count of leads touched in this run, grouped by pipeline stage.</p>
</td>
</tr>
<tr>
<td style="padding:0 28px 28px;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" bgcolor="#0a3550" style="background-color:#0a3550;border-radius:6px;">
{stageRows}</table>
{recipientPreviewSectionsHtml}
<p style="margin:18px 0 0;font-size:11px;line-height:1.45;color:#6a93a8;">This is an automated report from Lead Scoring. Please do not reply to this message.</p>
</td>
</tr>
</table>
</td>
</tr>
</table>
</body>
</html>
""";
    }

    private static int MapStage(LeadStage stage) => stage switch
    {
        LeadStage.Cold => 0,
        LeadStage.Warm => 1,
        LeadStage.Mql => 2,
        LeadStage.Hot => 3,
        _ => 4
    };

    private static string ResolveTemplate(string value, Lead lead, string eventName, bool trackingEnabled)
    {
        var leadId = lead.Id.ToString("D");
        var emailValue = trackingEnabled ? Uri.EscapeDataString(lead.Email) : lead.Email;
        var eventValue = trackingEnabled ? Uri.EscapeDataString(eventName) : eventName;
        var leadIdValue = trackingEnabled ? Uri.EscapeDataString(leadId) : leadId;

        return value
            .Replace("{{email}}", emailValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{event}}", eventValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{leadId}}", leadIdValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{stage}}", lead.Stage.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string BuildTrackedClickUrl(Lead lead, string destinationUrl)
    {
        if (!Uri.IsWellFormedUriString(destinationUrl, UriKind.Absolute))
        {
            return destinationUrl;
        }

        var token = tokenService.CreateLeadToken(lead.Id);
        var trackingBaseUrl = (configuration["Tracking:BaseUrl"] ?? "http://localhost:8211").TrimEnd('/');
        return $"{trackingBaseUrl}/track/click?token={Uri.EscapeDataString(token)}&redirect={Uri.EscapeDataString(destinationUrl)}";
    }

    private readonly record struct BatchLeadTemplateContext(LeadStage StageUsedForTemplate, int? ProductId);
}
