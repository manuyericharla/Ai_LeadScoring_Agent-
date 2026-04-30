namespace LeadScoring.Api.Models;

public class Visitor
{
    public string VisitorId { get; set; } = string.Empty;
    public EventSource FirstSource { get; set; } = EventSource.Unknown;
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
