namespace LeadScoring.Api.Services;

public interface ITenantContext
{
    bool IsAuthenticated { get; }
    string? CompanyName { get; }
    string? SchemaName { get; }
    Guid? TenantId { get; }
    void RequireTenant();
}
