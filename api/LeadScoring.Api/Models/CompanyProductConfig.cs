namespace LeadScoring.Api.Models;

public class CompanyProductConfig
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductEventConfigJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
}
