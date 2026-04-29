using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;
using System.Net;

namespace LeadScoring.Api.Services;

public class BatchProcessingService(
    IBatchRepository batchRepository,
    IUserSignupStatusService userSignupStatusService,
    IEmailService emailService,
    ILogger<BatchProcessingService> logger) : IBatchProcessingService
{
    private static readonly DateTime DefaultStartDate = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan DayInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan WeekInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan MonthInterval = TimeSpan.FromDays(30);

    public async Task ProcessActiveConfigsAsync(CancellationToken cancellationToken)
    {
        var activeConfigs = await batchRepository.GetActiveConfigsAsync(cancellationToken);
        foreach (var config in activeConfigs)
        {
            await ProcessConfigAsync(config, cancellationToken);
        }
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
            var result = await ProcessLeadAsync(batchLead.LeadId, cancellationToken);
            if (result.IsSuccess)
            {
                batchLead.Status = BatchLeadStatus.Success;
                batchLead.ErrorMessage = null;
                successCount++;
            }
            else
            {
                batchLead.Status = BatchLeadStatus.Failed;
                batchLead.ErrorMessage = result.ErrorMessage;
                failedCount++;
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

    private async Task ProcessConfigAsync(BatchConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var windowsToRun = GetWindowsToRun(config, nowUtc);
            if (windowsToRun.Count == 0)
            {
                return;
            }

            var sinceUtc = config.UpdatedAt ?? DefaultStartDate;
            var leads = await batchRepository.GetLeadsAfterAsync(config.ProductId, config.Stage, sinceUtc, cancellationToken);

            foreach (var batchType in windowsToRun)
            {
                await ProcessBatchWindowAsync(config, batchType, leads, cancellationToken);
            }

            config.UpdatedAt = nowUtc;
            await batchRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed processing config {ConfigId} for product {ProductId}", config.ConfigId, config.ProductId);
        }
    }

    private async Task ProcessBatchWindowAsync(BatchConfig config, BatchType batchType, List<Lead> leads, CancellationToken cancellationToken)
    {
        var batch = new Batch
        {
            ProductId = config.ProductId,
            BatchType = batchType,
            StartTime = DateTime.UtcNow,
            Status = BatchStatus.Running,
            CreatedAt = DateTime.UtcNow
        };

        await batchRepository.CreateBatchAsync(batch, cancellationToken);

        var batchLeads = leads.Select(lead => new BatchLead
        {
            BatchId = batch.BatchId,
            LeadId = lead.Id,
            Status = BatchLeadStatus.Pending,
            RetryCount = 0
        }).ToList();

        await batchRepository.CreateBatchLeadsAsync(batchLeads, cancellationToken);

        var successCount = 0;
        var failedCount = 0;
        foreach (var batchLead in batchLeads)
        {
            var result = await ProcessLeadAsync(batchLead.LeadId, cancellationToken);
            if (result.IsSuccess)
            {
                batchLead.Status = BatchLeadStatus.Success;
                batchLead.ErrorMessage = null;
                successCount++;
            }
            else
            {
                batchLead.Status = BatchLeadStatus.Failed;
                batchLead.ErrorMessage = result.ErrorMessage;
                failedCount++;
            }

            batchLead.ProcessedAt = DateTime.UtcNow;
        }

        batch.TotalLeads = batchLeads.Count;
        batch.SuccessCount = successCount;
        batch.FailedCount = failedCount;
        batch.Status = failedCount > 0 ? BatchStatus.Failed : BatchStatus.Completed;
        batch.EndTime = DateTime.UtcNow;
        await batchRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Processed {BatchType} batch {BatchId} for product {ProductId} at stage {Stage}. Total={Total}, Success={Success}, Failed={Failed}",
            batchType,
            batch.BatchId,
            batch.ProductId,
            config.Stage,
            batch.TotalLeads,
            batch.SuccessCount,
            batch.FailedCount);
    }

    private static List<BatchType> GetWindowsToRun(BatchConfig config, DateTime nowUtc)
    {
        var windows = new List<BatchType>();
        if (ShouldRun(config.Day, config.UpdatedAt, nowUtc, DayInterval))
        {
            windows.Add(BatchType.Daily);
        }

        if (ShouldRun(config.Week, config.UpdatedAt, nowUtc, WeekInterval))
        {
            windows.Add(BatchType.Weekly);
        }

        if (ShouldRun(config.Month, config.UpdatedAt, nowUtc, MonthInterval))
        {
            windows.Add(BatchType.Monthly);
        }

        return windows;
    }

    private static bool ShouldRun(bool enabled, DateTime? updatedAt, DateTime nowUtc, TimeSpan interval)
    {
        if (!enabled)
        {
            return false;
        }

        if (updatedAt is null)
        {
            return true;
        }

        return nowUtc - updatedAt.Value >= interval;
    }

    private async Task<LeadProcessResult> ProcessLeadAsync(Guid leadId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lead = await batchRepository.GetLeadForUpdateAsync(leadId, cancellationToken);
        if (lead is null)
        {
            return LeadProcessResult.Failure("Lead not found.");
        }

        UserSignupStatusData signupStatus;
        try
        {
            signupStatus = await userSignupStatusService.CheckUserSignupStatusAsync(lead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Signup status check failed for lead {LeadId}", leadId);
            return LeadProcessResult.Failure($"Signup status check failed: {ex.Message}");
        }

        lead.UserExists = signupStatus.UserExists;
        lead.SignupCompleted = signupStatus.SignupCompleted;
        lead.LoginDataExists = signupStatus.LoginDataExists;
        lead.ProfileCompletion = signupStatus.ProfileCompletion;
        lead.IsPlanSelected = signupStatus.IsPlanSelected;
        lead.SelectedPlan = signupStatus.SelectedPlan;
        lead.PlanRenewalDate = signupStatus.PlanRenewalDate;

        if (lead.ProfileCompletion)
        {
            await batchRepository.SaveChangesAsync(cancellationToken);
            return LeadProcessResult.Success();
        }

        if (!lead.UserExists)
        {
            await batchRepository.SaveChangesAsync(cancellationToken);
            return LeadProcessResult.Success();
        }

        var template = await batchRepository.GetActiveTemplateForStageAsync(lead.Stage, lead.ProductId, cancellationToken);
        if (template is null)
        {
            await batchRepository.SaveChangesAsync(cancellationToken);
            return LeadProcessResult.Success();
        }

        var eventName = $"batch_stage_{lead.Stage.ToString().ToLowerInvariant()}";
        var resolvedBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);
        if (!string.IsNullOrWhiteSpace(template.CtaButtonText) && !string.IsNullOrWhiteSpace(template.CtaLink))
        {
            var resolvedLink = ResolveTemplate(template.CtaLink, lead, eventName, template.IsTrackingEnabled);
            resolvedBody += $"""

                <p style="margin-top:20px;">
                  <a href="{resolvedLink}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                </p>
                """;
        }

        await emailService.SendAsync(lead.Email, template.Subject, resolvedBody);
        await batchRepository.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Type = EventType.WebsiteActivity,
            Source = EventSource.Email,
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = $$"""{"eventName":"{{eventName}}","templateName":"{{template.Name}}","systemMarker":"BatchStageEmailSent"}"""
        }, cancellationToken);

        await batchRepository.SaveChangesAsync(cancellationToken);
        return LeadProcessResult.Success();
    }

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

    private sealed record LeadProcessResult(bool IsSuccess, string? ErrorMessage)
    {
        public static LeadProcessResult Success() => new(true, null);
        public static LeadProcessResult Failure(string errorMessage) => new(false, errorMessage);
    }
}
