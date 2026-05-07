namespace LeadScoring.Api.Models;

public class AdminBatchReport
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
    public int Stage0Count { get; set; }
    public int Stage1Count { get; set; }
    public int Stage2Count { get; set; }
    public int Stage3Count { get; set; }
    public int Stage4Count { get; set; }
    public int BatchDailyCount { get; set; }
}
