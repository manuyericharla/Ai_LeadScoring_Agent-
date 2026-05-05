using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class LeadScoringService(
    LeadScoringDbContext db,
    IEmailService emailService,
    TokenService tokenService,
    IConfiguration configuration,
    IFollowUpSubjectGenerator followUpSubjectGenerator)
{
    public async Task AddEventAsync(LeadEvent leadEvent)
    {
        if (leadEvent.LeadId is null)
        {
            leadEvent.EventScore = 0;
            db.Events.Add(leadEvent);
            await db.SaveChangesAsync();
            return;
        }

        var lead = await db.Leads.FindAsync(leadEvent.LeadId.Value);
        if (lead is null)
        {
            throw new InvalidOperationException("Lead not found.");
        }

        var previousStage = lead.Stage;
        var scoreDelta = await GetScoreDeltaAsync(lead, leadEvent);
        leadEvent.EventScore = scoreDelta;
        db.Events.Add(leadEvent);
        lead.LastActivityUtc = leadEvent.TimestampUtc;
        if (scoreDelta > 0)
        {
            lead.Score += scoreDelta;
            lead.Stage = ResolveStage(lead.Score);
            lead.LastScoredAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // Stage-triggered emails are intentionally disabled.
    }

    public async Task CheckFirstEmailScoreUpdateAsync(TimeSpan scoreCheckDelay)
    {
        var threshold = DateTime.UtcNow.Subtract(scoreCheckDelay);
        var firstEmailEvents = await db.Events
            .Where(e =>
                e.TimestampUtc <= threshold &&
                e.MetadataJson != null &&
                e.MetadataJson.Contains("\"systemMarker\":\"WelcomeEmailSent\""))
            .ToListAsync();

        if (firstEmailEvents.Count == 0)
        {
            return;
        }

        var latestFirstEmails = firstEmailEvents
            .Where(e => e.LeadId != null)
            .GroupBy(e => e.LeadId)
            .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
            .ToList();

        var leadIds = latestFirstEmails.Select(x => x.LeadId!.Value).Distinct().ToList();
        var checks = await db.Events
            .Where(e =>
                e.LeadId != null &&
                leadIds.Contains(e.LeadId.Value) &&
                e.MetadataJson != null &&
                e.MetadataJson.Contains("\"systemMarker\":\"FirstEmailScoreChecked\""))
            .ToListAsync();

        foreach (var sentEvent in latestFirstEmails)
        {
            var sentLeadId = sentEvent.LeadId!.Value;
            var alreadyChecked = checks.Any(c => c.LeadId == sentLeadId && c.TimestampUtc >= sentEvent.TimestampUtc);
            if (alreadyChecked)
            {
                continue;
            }

            var lead = await db.Leads.FindAsync(sentLeadId);
            if (lead is null)
            {
                continue;
            }

            var scoreUpdated = await HasScoringEventAfterAsync(lead, sentEvent.TimestampUtc);

            db.Events.Add(new LeadEvent
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                Type = EventType.WebsiteActivity,
                Source = EventSource.Email,
                TimestampUtc = DateTime.UtcNow,
                MetadataJson = $$"""{"scoreUpdated":{{scoreUpdated.ToString().ToLowerInvariant()}},"firstEmailSentAt":"{{sentEvent.TimestampUtc:O}}","systemMarker":"FirstEmailScoreChecked"}"""
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task RunWelcomeFollowUpSchedulerAsync(TimeSpan followUpDelay, int maxAttemptsPerDay, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var thresholdUtc = nowUtc.Subtract(followUpDelay);
        var today = nowUtc.Date;

        var leads = await db.Leads
            .Where(x => x.WelcomeEmailSent && x.LastScoredAtUtc == null && x.LastActivityUtc <= thresholdUtc)
            .ToListAsync(cancellationToken);

        foreach (var lead in leads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var followUpEvents = await db.Events
                .Where(e =>
                    e.LeadId != null &&
                    e.LeadId == lead.Id &&
                    e.MetadataJson != null &&
                    e.MetadataJson.Contains("\"systemMarker\":\"WelcomeFollowUpEmailSent\""))
                .OrderByDescending(e => e.TimestampUtc)
                .ToListAsync(cancellationToken);

            var sentTodayCount = followUpEvents.Count(e => e.TimestampUtc.Date == today);
            if (sentTodayCount >= maxAttemptsPerDay)
            {
                continue;
            }

            var lastFollowUpSentAt = followUpEvents.FirstOrDefault()?.TimestampUtc;
            var intervalBaseUtc = lastFollowUpSentAt ?? lead.LastActivityUtc;
            if (nowUtc - intervalBaseUtc < followUpDelay)
            {
                continue;
            }

            var template = await db.EmailTemplates
                .Where(t => t.IsActive && t.IsFollowUp && t.Stage == LeadStage.Cold && (t.ProductId == lead.ProductId || t.ProductId == null))
                .OrderByDescending(t => t.ProductId == lead.ProductId)
                .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (template is null)
            {
                continue;
            }

            var attemptNumber = sentTodayCount + 1;
            var eventName = $"welcome_followup_{attemptNumber}";
            var resolvedBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);
            if (!string.IsNullOrWhiteSpace(template.CtaButtonText) && !string.IsNullOrWhiteSpace(template.CtaLink))
            {
                var resolvedLink = ResolveTemplate(template.CtaLink, lead, eventName, template.IsTrackingEnabled);
                var trackedLink = BuildTrackedClickUrl(lead, resolvedLink);
                resolvedBody += $"""

                    <p style="margin-top:20px;">
                      <a href="{trackedLink}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                    </p>
                    """;
            }

            var subject = template.Subject;
            if (attemptNumber is 2 or 3)
            {
                subject = await followUpSubjectGenerator.GenerateSubjectAsync(
                    template.Subject,
                    lead,
                    attemptNumber,
                    cancellationToken);
            }

            await emailService.SendAsync(lead.Email, subject, resolvedBody);

            lead.LastActivityUtc = nowUtc;
            db.Events.Add(new LeadEvent
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                Type = EventType.WebsiteActivity,
                Source = EventSource.Email,
                TimestampUtc = nowUtc,
                MetadataJson = $$"""{"eventName":"{{eventName}}","attempt":{{attemptNumber}},"subject":"{{JsonEncodedText.Encode(subject)}}","systemMarker":"WelcomeFollowUpEmailSent"}"""
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static LeadStage ResolveStage(int score)
    {
        return score switch
        {
            <= 30 => LeadStage.Cold,
            <= 60 => LeadStage.Warm,
            <= 99 => LeadStage.Mql,
            _ => LeadStage.Hot
        };
    }

    private async Task<int> GetScoreDeltaAsync(Lead lead, LeadEvent leadEvent)
    {
        if (!lead.ProductId.HasValue)
        {
            return 0;
        }

        var productConfig = await db.CompanyProductConfigs
            .Where(x => x.ProductId == lead.ProductId.Value)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (productConfig is null)
        {
            return 0;
        }

        var normalizedEventKey = NormalizeEventKey(ResolveEventKey(leadEvent));
        if (string.IsNullOrWhiteSpace(normalizedEventKey))
        {
            return 0;
        }

        foreach (var (configKey, score) in ParseConfig(productConfig.ProductEventConfigJson))
        {
            if (NormalizeEventKey(configKey) == normalizedEventKey)
            {
                return score;
            }
        }

        return 0;
    }

    private async Task<bool> HasScoringEventAfterAsync(Lead lead, DateTime sinceUtc)
    {
        var events = await db.Events
            .Where(e => e.LeadId == lead.Id && e.TimestampUtc > sinceUtc)
            .ToListAsync();

        foreach (var evt in events)
        {
            var delta = await GetScoreDeltaAsync(lead, evt);
            if (delta > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<KeyValuePair<string, int>> ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var score = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number when prop.Value.TryGetInt32(out var number) => number,
                    JsonValueKind.String when int.TryParse(prop.Value.GetString(), out var parsed) => parsed,
                    _ => 0
                };

                yield return new KeyValuePair<string, int>(prop.Name, score);
            }
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static string? ResolveEventKey(LeadEvent leadEvent)
    {
        if (!string.IsNullOrWhiteSpace(leadEvent.MetadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(leadEvent.MetadataJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("eventName", out var eventName) && eventName.ValueKind == JsonValueKind.String)
                    {
                        return eventName.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("eventType", out var eventType) && eventType.ValueKind == JsonValueKind.String)
                    {
                        return eventType.GetString();
                    }
                }
            }
            catch
            {
            }
        }

        return leadEvent.Type switch
        {
            EventType.BookDemo => "Book demo",
            EventType.Signup => "Signup",
            EventType.BlogPost => "Blogs",
            EventType.PricingPage => "Pricing page",
            EventType.EmailClick => "Email click",
            EventType.EmailCaptured => "Email capture",
            EventType.WebsiteActivity => "Website activity",
            _ => leadEvent.Type.ToString()
        };
    }

    private static string NormalizeEventKey(string? eventKey)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
        {
            return string.Empty;
        }

        var chars = eventKey
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private async Task SendStageEmailAsync(Lead lead)
    {
        var template = await db.EmailTemplates
            .Where(t => t.IsActive && t.Stage == lead.Stage && (t.ProductId == lead.ProductId || t.ProductId == null))
            .OrderByDescending(t => t.ProductId == lead.ProductId)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
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
            var trackedLink = BuildTrackedClickUrl(lead, resolvedLink);
            resolvedBody += $"""

                <p style="margin-top:20px;">
                  <a href="{trackedLink}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                </p>
                """;
        }

        var sentAtUtc = DateTime.UtcNow;
        await emailService.SendAsync(lead.Email, template.Subject, resolvedBody);

        if (lead.Stage == LeadStage.Cold && !template.IsFollowUp)
        {
            lead.WelcomeEmailSent = true;
            lead.LastActivityUtc = sentAtUtc;
        }
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

    private string BuildTrackedClickUrl(Lead lead, string destinationUrl)
    {
        if (!Uri.IsWellFormedUriString(destinationUrl, UriKind.Absolute))
        {
            return destinationUrl;
        }

        var token = tokenService.CreateLeadToken(lead.Id);
        var trackingBaseUrl = (configuration["Tracking:BaseUrl"] ?? "http://localhost:8211").TrimEnd('/');
        return $"{trackingBaseUrl}/track/click?token={Uri.EscapeDataString(token)}&redirect={Uri.EscapeDataString(destinationUrl)}";
    }
}
