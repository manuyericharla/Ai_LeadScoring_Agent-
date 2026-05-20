namespace LeadScoring.Api.Models;

public class Tenant
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string SelectedPlan { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
