namespace LeadScoring.Api.Models;

public class BatchLog
{
    public long BatchId { get; set; }
    public DateTime RunDate { get; set; }
    public CampaignBatchType BatchType { get; set; }
    public int TotalLeadsProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}
