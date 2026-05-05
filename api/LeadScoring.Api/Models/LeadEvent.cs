namespace LeadScoring.Api.Models;

public class LeadEvent
{
    public Guid Id { get; set; }
    public Guid? LeadId { get; set; }
    public int EventScore { get; set; }
    public string? VisitorId { get; set; }
    public string? Campaign { get; set; }
    public EventType Type { get; set; }
    public EventSource Source { get; set; }
    public bool SuspectedBot { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime TimestampUtc { get; set; }
    public Lead? Lead { get; set; }
}
