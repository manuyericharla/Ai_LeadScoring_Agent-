using System.Globalization;
using System.Security.Claims;

namespace LeadScoring.Api.Services;

public class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string? CompanyName => FindClaimValue("company");

    public string? SchemaName => FindClaimValue("tenant_db");

    public Guid? TenantId
    {
        get
        {
            var raw = FindClaimValue("tenant_id");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public void RequireTenant()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(SchemaName) || string.IsNullOrWhiteSpace(CompanyName))
        {
            throw new UnauthorizedAccessException("Company context is missing. Sign in again.");
        }
    }

    private string? FindClaimValue(string claimType)
    {
        var user = User;
        if (user is null)
        {
            return null;
        }

        return user.FindFirst(claimType)?.Value
            ?? user.FindFirst(c => c.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
