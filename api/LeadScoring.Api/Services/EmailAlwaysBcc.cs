using System.Net.Mail;

namespace LeadScoring.Api.Services;

internal static class EmailAlwaysBcc
{
    private const string ConfigKey = "Email:AlwaysBcc";

    public static IReadOnlyList<string> GetAddresses(IConfiguration configuration)
    {
        var fromSection = configuration.GetSection(ConfigKey).Get<string[]>();
        if (fromSection is { Length: > 0 })
        {
            return Normalize(fromSection);
        }

        var single = configuration[ConfigKey];
        if (!string.IsNullOrWhiteSpace(single))
        {
            return Normalize(single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return [];
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> raw) =>
        raw
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void AddDistinctBcc(MailAddressCollection bcc, string toAddress, IEnumerable<string> observerAddresses)
    {
        foreach (var addr in observerAddresses)
        {
            if (!string.Equals(addr, toAddress, StringComparison.OrdinalIgnoreCase))
            {
                bcc.Add(addr);
            }
        }
    }
}
