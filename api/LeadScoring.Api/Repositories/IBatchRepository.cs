using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;

namespace LeadScoring.Api.Repositories;

public interface IBatchRepository
{
    Task<bool> HasBatchRunOnDateAsync(DateTime runDateUtc, CancellationToken cancellationToken);
    Task<CampaignBatchType?> GetLastCompletedDailyBatchTypeAsync(CancellationToken cancellationToken);
    Task<List<Lead>> GetDay1LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken);
    Task<List<Lead>> GetDay2LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken);
    Task<List<Lead>> GetDay3LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken);
    Task<List<Lead>> GetDay4LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken);
    Task<List<Lead>> GetAllLeadsForPreviewAsync(CancellationToken cancellationToken);
    Task<BatchPreviewLeadAggregates> GetLeadAggregatesForPreviewAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<List<Lead>> GetLeadsDidNotOpenSinceLastEmailAsync(CancellationToken cancellationToken);
    Task<bool> HasBatchMarkerEventAsync(Guid leadId, string marker, DateTime fromUtc, CancellationToken cancellationToken);
    Task<bool> HasEngagementSinceLastEmailAsync(Guid leadId, DateTime lastEmailSentUtc, CancellationToken cancellationToken);
    Task<EmailTemplate?> GetTemplateByBatchTypeAsync(CampaignBatchType batchType, Lead lead, CancellationToken cancellationToken);
    Task<BatchLog> CreateBatchLogAsync(BatchLog batchLog, CancellationToken cancellationToken);
    Task<List<BatchLog>> GetRecentBatchLogsAsync(int take, CancellationToken cancellationToken);
    Task<AdminBatchReport> UpsertAdminReportAsync(
        string email,
        int stage0Count,
        int stage1Count,
        int stage2Count,
        int stage3Count,
        int stage4Count,
        CancellationToken cancellationToken);

    Task<List<BatchConfig>> GetActiveConfigsAsync(CancellationToken cancellationToken);
    Task<Batch?> GetBatchByIdAsync(long batchId, CancellationToken cancellationToken);
    Task<List<Lead>> GetLeadsAfterAsync(int productId, LeadStage stage, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<Batch> CreateBatchAsync(Batch batch, CancellationToken cancellationToken);
    Task CreateBatchLeadsAsync(IEnumerable<BatchLead> batchLeads, CancellationToken cancellationToken);
    Task<List<BatchLead>> GetFailedBatchLeadsAsync(long batchId, CancellationToken cancellationToken);
    Task<Lead?> GetLeadForUpdateAsync(Guid leadId, CancellationToken cancellationToken);
    Task<EmailTemplate?> GetActiveTemplateForStageAsync(LeadStage stage, int? productId, CancellationToken cancellationToken);
    Task AddEventAsync(LeadEvent leadEvent, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
