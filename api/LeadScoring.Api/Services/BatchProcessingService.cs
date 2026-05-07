using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;

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

            var sendResult = await ProcessLeadForBatchAsync(lead, batchType, marker, cancellationToken);
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

        await SendAdminSummaryAsync(runDateUtc, batchType, processed, success, failed, stageCounts, cancellationToken);
    }

    public async Task<BatchPreviewResultDto> PreviewAsync(CampaignBatchType batchType, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);

        var stage0Count = allLeads.Count(x => x.Stage == LeadStage.Cold);
        var stage1Count = allLeads.Count(x => x.Stage == LeadStage.Warm);
        var stage2Count = allLeads.Count(x => x.Stage == LeadStage.Mql);
        var stage3Count = allLeads.Count(x => x.Stage == LeadStage.Hot);
        var stage4Count = allLeads.Count - stage0Count - stage1Count - stage2Count - stage3Count;

        var newLeadsCount = allLeads.Count(x => x.CreatedAtUtc >= nowUtc.AddDays(-1));
        var last2DaysInactiveCount = allLeads.Count(x => x.LastActivityUtc <= nowUtc.AddDays(-2));
        var last4DaysSinceLastEmailCount = allLeads.Count(x => x.LastEmailSentDateUtc.HasValue && x.LastEmailSentDateUtc <= nowUtc.AddDays(-4));

        var didNotOpenCount = 0;
        foreach (var lead in allLeads.Where(x => x.LastEmailSentDateUtc.HasValue))
        {
            if (lead.LastEmailSentDateUtc is null)
            {
                continue;
            }

            var hasEngagement = await batchRepository.HasEngagementSinceLastEmailAsync(
                lead.Id,
                lead.LastEmailSentDateUtc.Value,
                cancellationToken);
            if (!hasEngagement)
            {
                didNotOpenCount++;
            }
        }

        return new BatchPreviewResultDto(
            batchType,
            allLeads.Count,
            stage0Count,
            stage1Count,
            stage2Count,
            stage3Count,
            stage4Count,
            newLeadsCount,
            last2DaysInactiveCount,
            last4DaysSinceLastEmailCount,
            didNotOpenCount,
            eligibleLeads.Count);
    }

    public async Task<BatchManualRunResultDto> RunManualAsync(CampaignBatchType batchType, string? scope, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var leads = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, scope, nowUtc, cancellationToken);
        var marker = $"DailySequence_{batchType}_Manual_{nowUtc:yyyyMMddHHmmss}";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var failures = new ConcurrentBag<BatchFailureInfoDto>();
        var stageCounts = new int[5];
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

            var sent = await ProcessLeadInIsolatedScopeAsync(lead.Id, batchType, marker, ct);
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

        await SendAdminSummaryAsync(nowUtc, batchType, processed, success, failed, stageCounts, cancellationToken);

        return new BatchManualRunResultDto(
            batchType,
            processed,
            success,
            failed,
            failures.ToList());
    }

    public async Task<BatchManualRunStartDto> StartManualAsync(CampaignBatchType batchType, string? scope, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "TotalEligible" : scope.Trim();
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var leads = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, normalizedScope, nowUtc, cancellationToken);
        var state = progressStore.CreateJob(batchType, normalizedScope, leads.Count);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                var result = await scopedService.RunManualTrackedAsync(state.JobId, batchType, normalizedScope, CancellationToken.None);
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

    public async Task<BatchManualRunResultDto> RunManualTrackedAsync(Guid jobId, CampaignBatchType batchType, string? scope, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var leads = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, scope, nowUtc, cancellationToken);
        var marker = $"DailySequence_{batchType}_Manual_{nowUtc:yyyyMMddHHmmss}";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var failures = new ConcurrentBag<BatchFailureInfoDto>();
        var stageCounts = new int[5];
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

                var sent = await ProcessLeadInIsolatedScopeAsync(lead.Id, batchType, marker, ct);
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

        await SendAdminSummaryAsync(nowUtc, batchType, processed, success, failed, stageCounts, cancellationToken);

        return new BatchManualRunResultDto(
            batchType,
            processed,
            success,
            failed,
            failures.ToList());
    }

    private async Task<bool> ProcessLeadInIsolatedScopeAsync(Guid leadId, CampaignBatchType batchType, string marker, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();

        var lead = await scopedRepository.GetLeadForUpdateAsync(leadId, cancellationToken);
        if (lead is null)
        {
            return false;
        }

        return await ProcessLeadForBatchAsync(scopedRepository, lead, batchType, marker, cancellationToken);
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
            var result = new List<Lead>();
            foreach (var lead in allLeads.Where(x => x.LastEmailSentDateUtc.HasValue))
            {
                if (!lead.LastEmailSentDateUtc.HasValue)
                {
                    continue;
                }

                var hasEngagement = await batchRepository.HasEngagementSinceLastEmailAsync(
                    lead.Id,
                    lead.LastEmailSentDateUtc.Value,
                    cancellationToken);
                if (!hasEngagement)
                {
                    result.Add(lead);
                }
            }

            return result;
        }

        return eligibleLeads;
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

    private async Task<bool> ProcessLeadForBatchAsync(Lead lead, CampaignBatchType batchType, string marker, CancellationToken cancellationToken)
        => await ProcessLeadForBatchAsync(batchRepository, lead, batchType, marker, cancellationToken);

    private async Task<bool> ProcessLeadForBatchAsync(IBatchRepository repository, Lead lead, CampaignBatchType batchType, string marker, CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateByBatchTypeAsync(batchType, lead, cancellationToken);
        if (template is null)
        {
            return false;
        }

        var eventName = $"batch_{batchType.ToString().ToLowerInvariant()}";
        var resolvedBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);
        if (!string.IsNullOrWhiteSpace(template.CtaButtonText) && !string.IsNullOrWhiteSpace(template.CtaLink))
        {
            var resolvedLink = ResolveTemplate(template.CtaLink, lead, eventName, template.IsTrackingEnabled);
            var trackedLink = BuildTrackedClickUrl(lead, resolvedLink);
            resolvedBody += $"""

                <p style="margin-top:20px;">
                  <a href="{trackedLink}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                </p>
                """;
        }

        var sent = await SendWithRetryAsync(lead.Email, template.Subject, resolvedBody, cancellationToken);
        if (!sent)
        {
            return false;
        }

        var sentAtUtc = DateTime.UtcNow;
        lead.LastEmailSentDateUtc = sentAtUtc;
        lead.LastActivityUtc = sentAtUtc;
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
        CancellationToken cancellationToken)
    {
        var adminEmailConfig = configuration["BatchProcessing:AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmailConfig))
        {
            logger.LogWarning("BatchProcessing:AdminEmail is not configured.");
            return;
        }

        var adminRecipients = adminEmailConfig
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (adminRecipients.Count == 0)
        {
            logger.LogWarning("BatchProcessing:AdminEmail is configured but no valid recipients were found.");
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

        var subject = $"[Batch Summary] {batchType} - {runDateUtc:yyyy-MM-dd}";
        var body = $"""
            <html><body>
            <h3>Lead Campaign Batch Summary</h3>
            <p><strong>Batch Type:</strong> {batchType}</p>
            <p><strong>Batch Execution (UTC):</strong> {runDateUtc:O}</p>
            <p><strong>Total leads processed:</strong> {totalProcessed}</p>
            <p><strong>Total successful emails sent:</strong> {successCount}</p>
            <p><strong>Total failed emails:</strong> {failureCount}</p>
            <h4>Stage-wise counts</h4>
            <ul>
              <li>Stage 0: {stageCounts[0]}</li>
              <li>Stage 1: {stageCounts[1]}</li>
              <li>Stage 2: {stageCounts[2]}</li>
              <li>Stage 3: {stageCounts[3]}</li>
              <li>Stage 4: {stageCounts[4]}</li>
            </ul>
            </body></html>
            """;

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
}
