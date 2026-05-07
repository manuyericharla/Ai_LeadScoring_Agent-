using LeadScoring.Api.Models;

namespace LeadScoring.Api.Contracts;

public record BatchRetryResultDto(
    long SourceBatchId,
    long RetryBatchId,
    int RetriedCount,
    int SuccessCount,
    int FailedCount,
    BatchStatus Status);

public record BatchPreviewResultDto(
    CampaignBatchType BatchType,
    int TotalLeadsCount,
    int Stage0Count,
    int Stage1Count,
    int Stage2Count,
    int Stage3Count,
    int Stage4Count,
    int NewLeadsCount,
    int Last2DaysInactiveCount,
    int Last4DaysSinceLastEmailCount,
    int DidNotOpenEmailCount,
    int TotalEligibleCount);

public record BatchManualRunRequestDto(
    string? Scope);

public record BatchFailureInfoDto(
    Guid LeadId,
    string Email,
    string Reason);

public record BatchManualRunResultDto(
    CampaignBatchType BatchType,
    int TotalLeads,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<BatchFailureInfoDto> Failures);

public record BatchManualRunStartDto(
    Guid JobId,
    CampaignBatchType BatchType,
    string Scope);

public record BatchManualRunStatusDto(
    Guid JobId,
    CampaignBatchType BatchType,
    string Scope,
    bool IsRunning,
    int TotalLeads,
    int ProcessedCount,
    int SuccessCount,
    int FailureCount,
    BatchManualRunResultDto? Result);
