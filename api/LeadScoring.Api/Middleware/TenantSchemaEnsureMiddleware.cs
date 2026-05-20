using LeadScoring.Api.Services;

namespace LeadScoring.Api.Middleware;

public class TenantSchemaEnsureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ITenantDatabaseProvisioner provisioner)
    {
        if (tenantContext.IsAuthenticated && !string.IsNullOrWhiteSpace(tenantContext.SchemaName))
        {
            await provisioner.EnsureReadyAsync(tenantContext.SchemaName!, context.RequestAborted);
        }

        await next(context);
    }
}
