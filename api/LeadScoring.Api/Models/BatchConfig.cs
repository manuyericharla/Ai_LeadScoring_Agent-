namespace LeadScoring.Api.Models;

public class BatchConfig
{
    public long ConfigId { get; set; }
    public int ProductId { get; set; }
    public LeadStage Stage { get; set; }
    public bool Day { get; set; }
    public bool Week { get; set; }
    public bool Month { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DailyRunCountDateUtc { get; set; }
    public int DailyRunCount { get; set; }
    public DateTime? LastDailyRunUtc { get; set; }
}
