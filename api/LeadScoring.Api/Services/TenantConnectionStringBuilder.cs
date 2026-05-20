using System.Text.RegularExpressions;

namespace LeadScoring.Api.Services;

public static class TenantConnectionStringBuilder
{
    private static readonly Regex SafeCompanyPart = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// PostgreSQL schema name for company-isolated lead data (same database, separate schema).
    /// </summary>
    public static string ToSchemaName(string companyName)
    {
        var slug = SafeCompanyPart.Replace(companyName.Trim().ToLowerInvariant(), "");
        if (string.IsNullOrEmpty(slug))
        {
            slug = "company";
        }

        slug = slug.Length > 40 ? slug[..40] : slug;
        return $"tenant_{slug}";
    }
}
