using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class TenantDbContextAccessor(
    ITenantContext tenantContext,
    IConfiguration configuration) : ITenantDbContextAccessor
{
    public LeadScoringDbContext GetDbContext()
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        if (tenantContext.IsAuthenticated)
        {
            tenantContext.RequireTenant();
            var schema = tenantContext.SchemaName!;

            var tenantOptions = new DbContextOptionsBuilder<LeadScoringDbContext>()
                .UseNpgsql(masterConnection, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", schema))
                .AddInterceptors(new TenantSchemaConnectionInterceptor(schema))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;

            return new LeadScoringDbContext(tenantOptions, schema);
        }

        var defaultOptions = new DbContextOptionsBuilder<LeadScoringDbContext>()
            .UseNpgsql(masterConnection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new LeadScoringDbContext(defaultOptions);
    }
}
