using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public interface ITenantLeadScope
{
    string? GetCurrentUserEmail();

    Task<string> ResolveCompanyNameAsync(CancellationToken cancellationToken = default);

    Task EnsureTenantContextMatchesUserAsync(CancellationToken cancellationToken = default);

    IQueryable<Lead> ApplyScope(IQueryable<Lead> leads, string companyName);
}
