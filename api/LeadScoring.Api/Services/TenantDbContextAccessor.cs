using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class TenantDbContextAccessor(
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration) : ITenantDbContextAccessor
{
    public LeadScoringDbContext GetDbContext()
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        var tenantSchema = httpContextAccessor.HttpContext?.User.FindFirst("tenant_db")?.Value;
        if (!string.IsNullOrWhiteSpace(tenantSchema))
        {
            var tenantOptions = new DbContextOptionsBuilder<LeadScoringDbContext>()
                .UseNpgsql(masterConnection, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", tenantSchema))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;
            return new LeadScoringDbContext(tenantOptions, tenantSchema);
        }

        var defaultOptions = new DbContextOptionsBuilder<LeadScoringDbContext>()
            .UseNpgsql(masterConnection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new LeadScoringDbContext(defaultOptions);
    }
}
