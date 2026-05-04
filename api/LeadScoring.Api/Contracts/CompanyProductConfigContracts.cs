using System.Text.Json;

namespace LeadScoring.Api.Contracts;

public sealed class UpsertCompanyProductConfigRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Optional; ignored. ProductId is assigned by the server on create and preserved on update.</summary>
    public int ProductId { get; set; }
    public Dictionary<string, int> ProductEventConfig { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyProductConfigDto
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public Dictionary<string, int> ProductEventConfig { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAtUtc { get; set; }
}

public static class CompanyProductConfigMapper
{
    public static CompanyProductConfigDto ToDto(
        Guid id,
        string companyName,
        string productName,
        int productId,
        string productEventConfigJson,
        DateTime createdAtUtc)
    {
        Dictionary<string, int>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(productEventConfigJson);
        }
        catch
        {
            parsed = null;
        }

        return new CompanyProductConfigDto
        {
            Id = id,
            CompanyName = companyName,
            ProductName = productName,
            ProductId = productId,
            ProductEventConfig = parsed ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            CreatedAtUtc = createdAtUtc
        };
    }
}
