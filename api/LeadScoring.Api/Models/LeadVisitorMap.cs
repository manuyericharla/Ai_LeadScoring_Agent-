namespace LeadScoring.Api.Models;

public class LeadVisitorMap
{
    public long Id { get; set; }
    public Guid LeadId { get; set; }
    public string VisitorId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public Lead Lead { get; set; } = null!;
}
