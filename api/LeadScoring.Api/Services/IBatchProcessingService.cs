using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public interface IBatchProcessingService
{
    Task ProcessActiveConfigsAsync(CancellationToken cancellationToken);
    Task<BatchRetryResultDto> RetryFailedLeadsAsync(long sourceBatchId, CancellationToken cancellationToken);
    Task<BatchPreviewResultDto> PreviewAsync(CampaignBatchType batchType, CancellationToken cancellationToken);
    Task<BatchManualRunResultDto> RunManualAsync(CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken);
    Task<BatchManualRunResultDto> RunManualTrackedAsync(Guid jobId, CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken);
    Task<BatchManualRunStartDto> StartManualAsync(CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken);
    BatchManualRunStatusDto? GetManualStatus(Guid jobId);
    Task<IReadOnlyList<BatchLogHistoryDto>> GetBatchLogHistoryAsync(int take, CancellationToken cancellationToken);
}
