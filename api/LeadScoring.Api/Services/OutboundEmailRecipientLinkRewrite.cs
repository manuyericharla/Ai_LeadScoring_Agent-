using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace LeadScoring.Api.Services;

/// <summary>
/// Rich HTML templates often ship with static <c>email=</c> query values from design tools; placeholder
/// replacement only affects <c>{{email}}</c>. This pass overwrites the attribution <c>email</c> parameter
/// on allowlisted HiperBrains URLs, including destinations nested in <c>/track/click?redirect=…</c>.
/// </summary>
internal static partial class OutboundEmailRecipientLinkRewrite
{
    public static string ApplyRecipientEmailToHiperbrainsLinks(string htmlBody, string recipientEmail)
    {
        if (string.IsNullOrWhiteSpace(htmlBody) || string.IsNullOrWhiteSpace(recipientEmail))
        {
            return htmlBody;
        }

        var trimmed = recipientEmail.Trim();
        var afterHref = HrefAttribute().Replace(htmlBody, m =>
        {
            var rawUrl = m.Groups["d"].Success
                ? m.Groups["d"].Value
                : m.Groups["s"].Success
                    ? m.Groups["s"].Value
                    : m.Groups["u"].Value;
            var rewritten = RewriteSingleHrefUrl(rawUrl, trimmed);
            if (rewritten == rawUrl)
            {
                return m.Value;
            }

            if (m.Groups["u"].Success)
            {
                return $"href={rewritten}";
            }

            var quote = m.Groups["d"].Success ? "\"" : "'";
            return $"href={quote}{rewritten}{quote}";
        });

        return HiperbrainsHttpsUrlLiterals().Replace(afterHref, m =>
        {
            var raw = m.Value.TrimEnd('.', ',', ';', '"').Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            var rewritten = TryRewriteHiperbrainsDestination(raw, trimmed);
            return rewritten ?? m.Value;
        });
    }

    private static string RewriteSingleHrefUrl(string rawUrl, string recipientEmail)
    {
        rawUrl = WebUtility.HtmlDecode(rawUrl.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim());
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return rawUrl;
        }

        if (uri.AbsolutePath.EndsWith("/track/click", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = QueryHelpers.ParseQuery(uri.Query.TrimStart('?'));
            if (!parsed.TryGetValue("redirect", out var redirectParts) || redirectParts.Count == 0)
            {
                return rawUrl;
            }

            var inner = redirectParts[^1];
            if (string.IsNullOrEmpty(inner))
            {
                return rawUrl;
            }

            var rewrittenInner = TryRewriteHiperbrainsDestination(inner, recipientEmail);
            if (rewrittenInner is null)
            {
                return rawUrl;
            }

            var outerDict = ToDictionary(parsed);
            outerDict["redirect"] = rewrittenInner;
            var ub = new UriBuilder(uri) { Query = ToQueryString(outerDict) };
            return ub.Uri.AbsoluteUri;
        }

        return TryRewriteHiperbrainsDestination(rawUrl, recipientEmail) ?? rawUrl;
    }

    private static string? TryRewriteHiperbrainsDestination(string absoluteUrl, string recipientEmail)
    {
        if (!RedirectSafety.TryGetAllowedAbsoluteRedirect(absoluteUrl, out var safe) || safe is null)
        {
            return null;
        }

        var ub = new UriBuilder(safe);
        var dict = ParseQueryString(ub.Query);
        dict["email"] = recipientEmail.Trim();
        ub.Query = ToQueryString(dict);
        return ub.Uri.AbsoluteUri;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var q = query.StartsWith("?", StringComparison.Ordinal) ? query[1..] : query;
        return ToDictionary(QueryHelpers.ParseQuery(q));
    }

    private static Dictionary<string, string> ToDictionary(IReadOnlyDictionary<string, Microsoft.Extensions.Primitives.StringValues> parsed)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in parsed)
        {
            if (kv.Value.Count > 0)
            {
                dict[kv.Key] = kv.Value[^1] ?? string.Empty;
            }
        }

        return dict;
    }

    private static string ToQueryString(Dictionary<string, string> dict)
    {
        var qs = QueryString.Create(dict.Select(static kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)));
        return qs.ToUriComponent().TrimStart('?');
    }

    /// <summary>Quoted and unquoted (common in WYSIWYG / ESP exports) href values.</summary>
    [GeneratedRegex(
        @"href\s*=\s*(?:""(?<d>[^""]*)""|'(?<s>[^']*)'|(?<u>https://[^\s<>""']+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefAttribute();

    /// <summary>
    /// Catches marketing URLs missed by strict href parsing (plain-text MIME part, folded lines, CMS quirks).
    /// </summary>
    [GeneratedRegex(
        @"https://(?:www\.)?hiperbrains\.com[^\s<>""')]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiperbrainsHttpsUrlLiterals();
}
