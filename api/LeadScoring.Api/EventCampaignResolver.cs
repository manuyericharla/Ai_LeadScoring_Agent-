using System.Text.Json;

namespace LeadScoring.Api;

/// <summary>Resolves <see cref="Models.LeadEvent.Campaign"/> from the explicit API field and/or JSON metadata.</summary>
public static class EventCampaignResolver
{
    private static readonly string[] MetadataKeys = ["campaign", "utm_campaign", "cmp", "ls_cmp"];

    public static string? Resolve(string? campaignParam, string? metadataJson)
    {
        if (!string.IsNullOrWhiteSpace(campaignParam))
        {
            return campaignParam.Trim();
        }

        return FromMetadata(metadataJson);
    }

    public static string? FromMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var key in MetadataKeys)
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop))
                {
                    continue;
                }

                var s = prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetRawText(),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(s))
                {
                    return s.Trim();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
