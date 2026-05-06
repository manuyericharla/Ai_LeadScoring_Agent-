using System.Text.Json;
using LeadScoring.Api.Models;

namespace LeadScoring.Api;

public static class LeadEventDisplay
{
    public static string FormatSource(EventSource src) => src switch
    {
        EventSource.Unknown => "Unknown",
        EventSource.Email => "Email",
        EventSource.Website => "Website",
        EventSource.LinkedIn => "LinkedIn",
        EventSource.Direct => "Direct",
        EventSource.Organic => "Organic",
        _ => "Unknown"
    };

    /// <summary>Human-readable label for what happened (from metadata when possible).</summary>
    public static string DescribeWhat(string? metadataJson, EventType type)
    {
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(metadataJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    if (TryString(root, "eventName", out var s))
                    {
                        return s!;
                    }

                    if (TryString(root, "action", out var action))
                    {
                        if (TryString(root, "page", out var page))
                        {
                            return $"{action}: {page}";
                        }

                        if (TryString(root, "url", out var url))
                        {
                            return $"{action}: {Shorten(url!)}";
                        }

                        return action!;
                    }

                    if (TryString(root, "subject", out var subject))
                    {
                        return subject!;
                    }

                    if (TryString(root, "page", out var pageOnly))
                    {
                        return pageOnly!;
                    }

                    if (TryString(root, "url", out var urlOnly))
                    {
                        return Shorten(urlOnly!);
                    }
                }
            }
            catch (JsonException)
            {
                /* fall through */
            }
        }

        return type.ToString();
    }

    private static bool TryString(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var t = prop.GetString();
        if (string.IsNullOrWhiteSpace(t))
        {
            return false;
        }

        value = t.Trim();
        return true;
    }

    private static string Shorten(string url)
    {
        const int max = 96;
        if (url.Length <= max)
        {
            return url;
        }

        return url[..(max - 1)] + "…";
    }
}
