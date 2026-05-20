using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class TenantLeadScope(
    IHttpContextAccessor httpContextAccessor,
    MasterDbContext masterDb,
    ITenantContext tenantContext) : ITenantLeadScope
{
    public const int ScopedProductId = 1;

    public string? GetCurrentUserEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return null;
        }

        return user.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? user.FindFirstValue(ClaimTypes.Email);
    }

    public async Task<string> ResolveCompanyNameAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveUserTenantAsync(cancellationToken);
        return tenant.CompanyName;
    }

    public async Task EnsureTenantContextMatchesUserAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveUserTenantAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(tenantContext.SchemaName)
            && !string.Equals(tenantContext.SchemaName, tenant.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Tenant context mismatch. Sign in again.");
        }

        if (!string.IsNullOrWhiteSpace(tenantContext.CompanyName)
            && !string.Equals(
                tenantContext.CompanyName.Trim(),
                tenant.CompanyName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Company context mismatch. Sign in again.");
        }
    }

    public IQueryable<Lead> ApplyScope(IQueryable<Lead> leads, string companyName)
    {
        var normalizedCompany = companyName.Trim();
        return leads.Where(l =>
            l.ProductId == ScopedProductId
            && l.CompanyId != null
            && EF.Functions.ILike(l.CompanyId, normalizedCompany));
    }

    private async Task<(string CompanyName, string DatabaseName)> ResolveUserTenantAsync(
        CancellationToken cancellationToken)
    {
        var email = GetCurrentUserEmail();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new UnauthorizedAccessException("User email is missing. Sign in again.");
        }

        if (tenantContext.TenantId is Guid tenantId)
        {
            var byTenantId = await masterDb.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.CompanyName, t.DatabaseName })
                .FirstOrDefaultAsync(cancellationToken);

            if (byTenantId is not null && !string.IsNullOrWhiteSpace(byTenantId.CompanyName))
            {
                return (byTenantId.CompanyName.Trim(), byTenantId.DatabaseName.Trim());
            }
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var tenant = await (
                from u in masterDb.Users.AsNoTracking()
                join t in masterDb.Tenants.AsNoTracking() on u.TenantId equals t.Id
                where u.Email.ToLower() == normalizedEmail
                select new { t.CompanyName, t.DatabaseName })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is not null && !string.IsNullOrWhiteSpace(tenant.CompanyName))
        {
            return (tenant.CompanyName.Trim(), tenant.DatabaseName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(tenantContext.CompanyName)
            && !string.IsNullOrWhiteSpace(tenantContext.SchemaName))
        {
            return (tenantContext.CompanyName.Trim(), tenantContext.SchemaName.Trim());
        }

        throw new UnauthorizedAccessException("Company context could not be resolved for this user.");
    }
}
