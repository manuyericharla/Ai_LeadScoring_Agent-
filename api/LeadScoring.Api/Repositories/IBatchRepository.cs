using LeadScoring.Api.Models;

namespace LeadScoring.Api.Repositories;

public interface IBatchRepository
{
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
