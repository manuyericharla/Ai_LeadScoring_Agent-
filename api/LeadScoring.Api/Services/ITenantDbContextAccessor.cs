using LeadScoring.Api.Data;

namespace LeadScoring.Api.Services;

public interface ITenantDbContextAccessor
{
    LeadScoringDbContext GetDbContext();
}
