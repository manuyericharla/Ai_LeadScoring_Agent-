using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;
using System.Net;
using System.Net.Mail;

namespace LeadScoring.Api.Services;

public class BatchProcessingService(
    IBatchRepository batchRepository,
    IEmailService emailService,
    TokenService tokenService,
    IConfiguration configuration,
    ILogger<BatchProcessingService> logger) : IBatchProcessingService
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
    {
        var template = await batchRepository.GetTemplateByBatchTypeAsync(batchType, lead, cancellationToken);
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

        await batchRepository.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Type = EventType.WebsiteActivity,
            Source = EventSource.Email,
            TimestampUtc = sentAtUtc,
            MetadataJson = $$"""{"eventName":"{{eventName}}","batchType":"{{batchType}}","systemMarker":"{{marker}}"}"""
        }, cancellationToken);

        await batchRepository.SaveChangesAsync(cancellationToken);
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
        var adminEmail = configuration["BatchProcessing:AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            logger.LogWarning("BatchProcessing:AdminEmail is not configured.");
            return;
        }

        await batchRepository.UpsertAdminReportAsync(
            adminEmail,
            stageCounts[0],
            stageCounts[1],
            stageCounts[2],
            stageCounts[3],
            stageCounts[4],
            cancellationToken);

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

        await SendAdminEmailWithoutBccAsync(adminEmail, subject, body, cancellationToken);
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
