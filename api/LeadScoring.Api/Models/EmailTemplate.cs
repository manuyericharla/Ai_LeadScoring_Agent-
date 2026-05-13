namespace LeadScoring.Api.Models;

public class EmailTemplate
{
    public int TemplateId { get; set; }
    public int? ProductId { get; set; }
    public bool IsFollowUp { get; set; }
    public string Name { get; set; } = string.Empty;
    public LeadStage Stage { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string EmailBodyHtml { get; set; } = string.Empty;
    /// <summary>Legacy column; outbound mail uses <see cref="EmailBodyHtml"/> only (no appended CTA from this field).</summary>
    public string? CtaButtonText { get; set; }

    /// <summary>Legacy column; outbound mail uses <see cref="EmailBodyHtml"/> only (no appended CTA from this field).</summary>
    public string? CtaLink { get; set; }
    public bool IsTrackingEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
