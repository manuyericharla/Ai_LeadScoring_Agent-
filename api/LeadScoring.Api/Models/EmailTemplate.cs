namespace LeadScoring.Api.Models;

public class EmailTemplate
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public LeadStage Stage { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string EmailBodyHtml { get; set; } = string.Empty;
    public string? CtaButtonText { get; set; }
    public string? CtaLink { get; set; }
    public bool IsTrackingEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
