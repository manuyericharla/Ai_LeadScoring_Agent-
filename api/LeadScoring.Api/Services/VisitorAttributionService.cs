using System.Text.Json;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class VisitorAttributionService(
    LeadScoringDbContext db,
    LeadScoringService leadScoringService)
{
    public async Task<Visitor> EnsureVisitorAsync(string visitorId, EventSource source, string? userAgent, string? ipAddress)
    {
        var normalizedVisitorId = visitorId.Trim();
        var visitor = await db.Visitors.FirstOrDefaultAsync(x => x.VisitorId == normalizedVisitorId);
        if (visitor is not null)
        {
            return visitor;
        }

        visitor = new Visitor
        {
            VisitorId = normalizedVisitorId,
            FirstSource = source,
            UserAgent = userAgent,
            IpAddress = ipAddress,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Visitors.Add(visitor);
        await db.SaveChangesAsync();
        return visitor;
    }

    public async Task TrackAnonymousEventAsync(
        string visitorId,
        EventSource source,
        EventType eventType,
        string? metadataJson,
        string? campaign,
        Guid? leadId = null)
    {
        await EnsureVisitorAsync(visitorId, source, null, null);

        var leadEvent = new LeadEvent
        {
            Id = Guid.NewGuid(),
            VisitorId = visitorId.Trim(),
            LeadId = leadId,
            Source = source,
            Type = eventType,
            Campaign = string.IsNullOrWhiteSpace(campaign) ? null : campaign.Trim(),
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = metadataJson
        };

        if (leadId.HasValue)
        {
            await leadScoringService.AddEventAsync(leadEvent);
            return;
        }

        db.Events.Add(leadEvent);
        await db.SaveChangesAsync();
    }

    public async Task<(Lead Lead, bool LeadCreated, bool VisitorMapped)> IdentifyVisitorAsync(WebsiteDemoSubmitRequest request)
    {
        var visitorId = request.VisitorId.Trim();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var nowUtc = DateTime.UtcNow;

        var visitor = await db.Visitors.FirstOrDefaultAsync(x => x.VisitorId == visitorId);
        var visitorSource = visitor?.FirstSource ?? ParseSource(request.Source);
        if (visitor is null)
        {
            visitor = new Visitor
            {
                VisitorId = visitorId,
                FirstSource = visitorSource,
                UserAgent = null,
                IpAddress = null,
                CreatedAtUtc = nowUtc
            };
            db.Visitors.Add(visitor);
        }

        var lead = await db.Leads.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Email, normalizedEmail));
        var leadCreated = false;
        if (lead is null)
        {
            lead = new Lead
            {
                Id = Guid.NewGuid(),
                VisitorId = visitorId,
                Email = normalizedEmail,
                FirstName = request.FirstName?.Trim(),
                LastName = request.LastName?.Trim(),
                ProductId = request.ProductId,
                WelcomeEmailSent = false,
                Score = 0,
                Stage = LeadStage.Cold,
                CreatedAtUtc = nowUtc,
                LastActivityUtc = nowUtc,
                FirstSource = visitorSource,
                LastSource = visitorSource
            };
            db.Leads.Add(lead);
            leadCreated = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(lead.VisitorId))
            {
                lead.VisitorId = visitorId;
            }

            lead.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? lead.FirstName : request.FirstName.Trim();
            lead.LastName = string.IsNullOrWhiteSpace(request.LastName) ? lead.LastName : request.LastName.Trim();
            lead.ProductId = request.ProductId ?? lead.ProductId;
            lead.LastActivityUtc = nowUtc;
            lead.LastSource = visitorSource;
            if (lead.FirstSource is null)
            {
                lead.FirstSource = visitorSource;
            }
        }

        var existingMap = await db.LeadVisitorMaps
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LeadId == lead.Id && x.VisitorId == visitorId);

        var visitorMapped = false;
        if (existingMap is null)
        {
            db.LeadVisitorMaps.Add(new LeadVisitorMap
            {
                LeadId = lead.Id,
                VisitorId = visitorId,
                CreatedAtUtc = nowUtc
            });
            visitorMapped = true;
        }

        await db.Events
            .Where(x => x.VisitorId == visitorId && x.LeadId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeadId, lead.Id));

        db.Events.Add(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            VisitorId = visitorId,
            Type = EventType.BookDemo,
            Source = visitorSource,
            TimestampUtc = nowUtc,
            Campaign = string.IsNullOrWhiteSpace(request.Campaign) ? null : request.Campaign.Trim(),
            MetadataJson = BuildWebsiteDemoMetadata(request, visitorId)
        });

        await db.SaveChangesAsync();
        return (lead, leadCreated, visitorMapped);
    }

    public async Task<(Lead Lead, bool LeadCreated, bool VisitorMapped)> IdentifyLeadByEmailAsync(LeadIdentifyRequest request)
    {
        var visitorId = request.VisitorId.Trim();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var nowUtc = DateTime.UtcNow;

        var visitor = await db.Visitors.FirstOrDefaultAsync(x => x.VisitorId == visitorId);
        var visitorSource = visitor?.FirstSource ?? ParseSource(request.Source);
        if (visitor is null)
        {
            visitor = new Visitor
            {
                VisitorId = visitorId,
                FirstSource = visitorSource,
                UserAgent = null,
                IpAddress = null,
                CreatedAtUtc = nowUtc
            };
            db.Visitors.Add(visitor);
        }

        var lead = await db.Leads.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Email, normalizedEmail));
        var leadCreated = false;
        if (lead is null)
        {
            lead = new Lead
            {
                Id = Guid.NewGuid(),
                VisitorId = visitorId,
                Email = normalizedEmail,
                ProductId = null,
                WelcomeEmailSent = false,
                Score = 0,
                Stage = LeadStage.Cold,
                CreatedAtUtc = nowUtc,
                LastActivityUtc = nowUtc,
                FirstSource = visitorSource,
                LastSource = visitorSource
            };
            db.Leads.Add(lead);
            leadCreated = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(lead.VisitorId))
            {
                lead.VisitorId = visitorId;
            }

            lead.LastActivityUtc = nowUtc;
            lead.LastSource = visitorSource;
            if (lead.FirstSource is null)
            {
                lead.FirstSource = visitorSource;
            }
        }

        var existingMap = await db.LeadVisitorMaps
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LeadId == lead.Id && x.VisitorId == visitorId);

        var visitorMapped = false;
        if (existingMap is null)
        {
            db.LeadVisitorMaps.Add(new LeadVisitorMap
            {
                LeadId = lead.Id,
                VisitorId = visitorId,
                CreatedAtUtc = nowUtc
            });
            visitorMapped = true;
        }

        await db.Events
            .Where(x => x.VisitorId == visitorId && x.LeadId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeadId, lead.Id));

        await db.SaveChangesAsync();
        return (lead, leadCreated, visitorMapped);
    }

    public static EventSource ParseSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return EventSource.Unknown;
        }

        return source.Trim().ToLowerInvariant() switch
        {
            "email" => EventSource.Email,
            "website" => EventSource.Website,
            "linkedin" => EventSource.LinkedIn,
            "linkdin" => EventSource.LinkedIn,
            "direct" => EventSource.Direct,
            "organic" => EventSource.Organic,
            _ => EventSource.Unknown
        };
    }

    public static string ToAttributionToken(EventSource source) =>
        source switch
        {
            EventSource.Email => "email",
            EventSource.Website => "website",
            EventSource.LinkedIn => "linkedin",
            EventSource.Direct => "direct",
            EventSource.Organic => "organic",
            _ => "unknown"
        };

    private static string BuildWebsiteDemoMetadata(WebsiteDemoSubmitRequest request, string visitorId)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["eventName"] = "Book demo",
            ["systemMarker"] = "WebsiteDemoSubmitted",
            ["visitorId"] = visitorId,
            ["email"] = request.Email.Trim().ToLowerInvariant(),
            ["firstName"] = request.FirstName,
            ["lastName"] = request.LastName,
            ["phoneNumber"] = request.PhoneNumber,
            ["country"] = request.Country,
            ["companyName"] = request.CompanyName,
            ["notes"] = request.Notes,
            ["campaign"] = request.Campaign,
            ["productId"] = request.ProductId
        };

        if (!string.IsNullOrWhiteSpace(request.MetadataJson))
        {
            metadata["rawMetadata"] = request.MetadataJson;
        }

        return JsonSerializer.Serialize(metadata);
    }

    public Task<bool> VisitorAlreadyIdentifiedAsync(string visitorId) =>
        db.LeadVisitorMaps.AsNoTracking().AnyAsync(x => x.VisitorId == visitorId.Trim());

    public async Task<string?> TryGetCapturedEmailAsync(string visitorId)
    {
        var trimmed = visitorId.Trim();
        var email = await db.LeadVisitorMaps
            .AsNoTracking()
            .Where(x => x.VisitorId == trimmed)
            .Join(db.Leads.AsNoTracking(), m => m.LeadId, l => l.Id, (_, l) => l.Email)
            .FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    /// <summary>Maps visitor to existing or new lead, attaches orphan events, records <see cref="EventType.EmailCaptured"/>.</summary>
    public async Task<(Lead Lead, bool LeadCreated, bool VisitorMapped)> CaptureEmailFromGateAsync(
        string visitorId,
        string emailRaw,
        EventSource requestedSource,
        string? campaign,
        long? dwellMs)
    {
        visitorId = visitorId.Trim();
        var normalizedEmail = emailRaw.Trim().ToLowerInvariant();
        var nowUtc = DateTime.UtcNow;

        var visitor = await db.Visitors.FirstOrDefaultAsync(x => x.VisitorId == visitorId);
        var visitorSource = visitor?.FirstSource ?? requestedSource;
        if (visitor is null)
        {
            visitor = new Visitor
            {
                VisitorId = visitorId,
                FirstSource = visitorSource,
                UserAgent = null,
                IpAddress = null,
                CreatedAtUtc = nowUtc
            };
            db.Visitors.Add(visitor);
        }

        var lead = await db.Leads.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Email, normalizedEmail));
        var leadCreated = false;
        if (lead is null)
        {
            lead = new Lead
            {
                Id = Guid.NewGuid(),
                VisitorId = visitorId,
                Email = normalizedEmail,
                WelcomeEmailSent = false,
                Score = 0,
                Stage = LeadStage.Cold,
                CreatedAtUtc = nowUtc,
                LastActivityUtc = nowUtc,
                FirstSource = visitorSource,
                LastSource = visitorSource
            };
            db.Leads.Add(lead);
            leadCreated = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(lead.VisitorId))
            {
                lead.VisitorId = visitorId;
            }

            lead.LastActivityUtc = nowUtc;
            lead.LastSource = visitorSource;
            if (lead.FirstSource is null)
            {
                lead.FirstSource = visitorSource;
            }
        }

        var existingMap =
            await db.LeadVisitorMaps.AsNoTracking().FirstOrDefaultAsync(x =>
                x.LeadId == lead.Id && x.VisitorId == visitorId);

        var visitorMapped = false;
        if (existingMap is null)
        {
            db.LeadVisitorMaps.Add(new LeadVisitorMap
            {
                LeadId = lead.Id,
                VisitorId = visitorId,
                CreatedAtUtc = nowUtc
            });
            visitorMapped = true;
        }

        await db.Events
            .Where(x => x.VisitorId == visitorId && x.LeadId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeadId, lead.Id));

        var gateMetadata = new Dictionary<string, object?>
        {
            ["eventName"] = "EmailCaptureGate",
            ["visitorId"] = visitorId,
            ["email"] = normalizedEmail,
            ["dwellMs"] = dwellMs
        };

        db.Events.Add(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            VisitorId = visitorId,
            Type = EventType.EmailCaptured,
            Source = visitorSource,
            TimestampUtc = nowUtc,
            Campaign = string.IsNullOrWhiteSpace(campaign) ? null : campaign.Trim(),
            MetadataJson = JsonSerializer.Serialize(gateMetadata)
        });

        await db.SaveChangesAsync();
        return (lead, leadCreated, visitorMapped);
    }
}
