using System.Text.Json.Serialization;

namespace LeadScoring.Api.Contracts;

public record LeadDashboardDto(
    Guid Id,
    string Email,
    int Score,
    string Stage,
    DateTime LastActivityUtc,
    DateTime? LastScoredAtUtc,
    string? LastEvent,
    [property: JsonPropertyName("lastEventSource")] string? LastEventSource,
    [property: JsonPropertyName("lastEventCampaign")] string? LastEventCampaign,
    string? LastEmailName,
    string? NextEmailName,
    DateTime? NextDateTimeUtc,
    string NextStage,
    bool SignupCompleted,
    bool ProfileCompletion,
    string? SelectedPlan,
    DateTime? PlanRenewalDate);
