using LeadScoring.Api.Data;

namespace LeadScoring.Api.Services;

/// <summary>
/// Shared lead/event data in <c>public."Leads"</c>, filtered by <see cref="Models.Lead.CompanyId"/>.
/// </summary>
public interface ICompanyLeadDbAccessor
{
    PublicCompanyDbContext GetDbContext();
}
