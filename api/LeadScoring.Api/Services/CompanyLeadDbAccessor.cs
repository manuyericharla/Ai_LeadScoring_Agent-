using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LeadScoring.Api.Services;

public class CompanyLeadDbAccessor(IConfiguration configuration) : ICompanyLeadDbAccessor
{
    public PublicCompanyDbContext GetDbContext()
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        var options = new DbContextOptionsBuilder<PublicCompanyDbContext>()
            .UseNpgsql(masterConnection)
            .AddInterceptors(new PublicSchemaConnectionInterceptor())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new PublicCompanyDbContext(options);
    }
}
