namespace LeadScoring.Api.Contracts;

public record TrackEventRequest(
    string VisitorId,
    string? Source,
    string? EventType,
    string? Campaign,
    string? MetadataJson,
    Guid? LeadId);
