namespace LeadScoring.Api.Contracts;

public record WebsiteDemoSubmitRequest(
    string VisitorId,
    string Email,
    string? Source,
    string? Campaign,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Country,
    string? CompanyName,
    string? Notes,
    int? ProductId,
    string? MetadataJson);

public record WebsiteDemoSubmitResponse(
    Guid LeadId,
    string Email,
    bool LeadCreated,
    bool VisitorMapped,
    bool EventCreated);

public record LeadEmailExistsRequest(string Email);

public record LeadEmailExistsResponse(
    string Email,
    bool Exists,
    Guid? LeadId);

public record LeadIdentifyRequest(
    string VisitorId,
    string Email,
    string? Source,
    string? Campaign);

public record LeadIdentifyResponse(
    Guid LeadId,
    string Email,
    bool LeadCreated,
    bool VisitorMapped);
