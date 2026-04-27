using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class LeadScoringService(LeadScoringDbContext db, IEmailService emailService)
{
    public async Task AddEventAsync(LeadEvent leadEvent)
    {
        var lead = await db.Leads.FindAsync(leadEvent.LeadId);
        if (lead is null)
        {
            throw new InvalidOperationException("Lead not found.");
        }

        var previousStage = lead.Stage;
        db.Events.Add(leadEvent);
        lead.LastActivityUtc = leadEvent.TimestampUtc;
        ApplyScore(lead, leadEvent.Type);
        lead.LastScoredAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (lead.Stage > previousStage)
        {
            await SendStageEmailAsync(lead);
        }
    }

    public async Task ReScoreInactiveLeadsAsync(TimeSpan inactivityThreshold)
    {
        var threshold = DateTime.UtcNow.Subtract(inactivityThreshold);
        var staleLeads = await db.Leads
            .Where(l => l.LastActivityUtc <= threshold)
            .ToListAsync();

        foreach (var lead in staleLeads)
        {
            lead.LastScoredAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static void ApplyScore(Lead lead, EventType eventType)
    {
        lead.Score += eventType switch
        {
            EventType.EmailClick => 10,
            EventType.WebsiteActivity => 20,
            _ => 0
        };

        lead.Stage = lead.Score switch
        {
            <= 30 => LeadStage.Cold,
            <= 60 => LeadStage.Warm,
            <= 99 => LeadStage.Mql,
            _ => LeadStage.Hot
        };
    }

    private async Task SendStageEmailAsync(Lead lead)
    {
        var template = await db.EmailTemplates
            .Where(t => t.IsActive && t.Stage == lead.Stage)
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync();

        if (template is null)
        {
            return;
        }

        var eventName = $"lead_stage_{lead.Stage.ToString().ToLowerInvariant()}";
        var resolvedBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);

        if (!string.IsNullOrWhiteSpace(template.CtaButtonText) && !string.IsNullOrWhiteSpace(template.CtaLink))
        {
            var resolvedLink = ResolveTemplate(template.CtaLink, lead, eventName, template.IsTrackingEnabled);
            resolvedBody += $"""

                <p style="margin-top:20px;">
                  <a href="{resolvedLink}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                </p>
                """;
        }

        await emailService.SendAsync(lead.Email, template.Subject, resolvedBody);
    }

    private static string ResolveTemplate(string value, Lead lead, string eventName, bool trackingEnabled)
    {
        var leadId = lead.Id.ToString("D");
        var emailValue = trackingEnabled ? Uri.EscapeDataString(lead.Email) : lead.Email;
        var eventValue = trackingEnabled ? Uri.EscapeDataString(eventName) : eventName;
        var leadIdValue = trackingEnabled ? Uri.EscapeDataString(leadId) : leadId;

        return value
            .Replace("{{email}}", emailValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{event}}", eventValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{leadId}}", leadIdValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{stage}}", lead.Stage.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
