namespace LeadScoring.Api.Services;

public interface ITenantDatabaseProvisioner
{
    Task ProvisionAsync(string schemaName, CancellationToken cancellationToken = default);
    Task EnsureReadyAsync(string schemaName, CancellationToken cancellationToken = default);
}
