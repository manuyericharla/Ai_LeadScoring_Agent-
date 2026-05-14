using System.Text.RegularExpressions;

namespace LeadScoring.Api.Services;

internal static partial class EmailBodyCtaPolicy
{
    /// <summary>
    /// When true, the email body is treated as already carrying CTAs (or opting out), so the green appended button is skipped.
    /// </summary>
    internal static bool ShouldSuppressAppendedCta(string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return false;
        }

        if (htmlBody.Contains("leadscoring:suppress-appended-cta", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AnchorOpenTag().IsMatch(htmlBody))
        {
            return true;
        }

        if (htmlBody.Contains("<area", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return htmlBody.Contains("class=\"cta-button\"", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<\s*a(\s|>|/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorOpenTag();
}
