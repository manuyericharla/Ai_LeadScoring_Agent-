namespace LeadScoring.Api.Contracts;

public record LeadEventsResponse(
    Guid LeadId,
    string Email,
    int TotalScore,
    string Stage,
    IReadOnlyList<LeadEventDetailDto> Events);

public record LeadEventDetailDto(
    Guid Id,
    DateTime TimestampUtc,
    int EventScore,
    string EventType,
    string Source,
    string? Campaign,
    string What);
