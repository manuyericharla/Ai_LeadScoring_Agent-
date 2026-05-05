using LeadScoring.Api;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(LeadScoringDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        const int nextEmailDelayHours = 24;

        var leads = await (
            from l in db.Leads
            let lastEv = db.Events
                .Where(e => e.LeadId == l.Id)
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefault()
            let lastNonEmptyCampaign = db.Events
                .Where(e => e.LeadId == l.Id && e.Campaign != null && e.Campaign != "")
                .OrderByDescending(e => e.TimestampUtc)
                .Select(e => e.Campaign)
                .FirstOrDefault()
            let src = lastEv != null ? (EventSource?)lastEv.Source : l.LastSource
            orderby l.LastActivityUtc descending
            select new LeadDashboardDto(
                l.Id,
                l.Email,
                l.Score,
                l.Stage.ToString(),
                l.LastActivityUtc,
                l.LastScoredAtUtc,
                lastEv != null ? lastEv.Type.ToString() : null,
                src == null
                    ? null
                    : src == EventSource.Unknown
                        ? "Unknown"
                        : src == EventSource.Email
                            ? "Email"
                            : src == EventSource.Website
                                ? "Website"
                                : src == EventSource.LinkedIn
                                    ? "LinkedIn"
                                    : src == EventSource.Direct
                                        ? "Direct"
                                        : src == EventSource.Organic
                                            ? "Organic"
                                            : "Unknown",
                lastNonEmptyCampaign,
                db.Events
                    .Where(e => e.LeadId == l.Id && e.Source == EventSource.Email)
                    .OrderByDescending(e => e.TimestampUtc)
                    .Select(e => e.MetadataJson != null && EF.Functions.Like(e.MetadataJson, "%welcome_email%")
                        ? "Welcome Email"
                        : e.Type.ToString())
                    .FirstOrDefault(),
                db.EmailTemplates
                    .Where(t => t.IsActive &&
                                t.Stage == (l.Stage == LeadStage.Cold
                                    ? LeadStage.Warm
                                    : l.Stage == LeadStage.Warm
                                        ? LeadStage.Mql
                                        : LeadStage.Hot) &&
                                (t.ProductId == l.ProductId || t.ProductId == null))
                    .OrderByDescending(t => t.ProductId == l.ProductId)
                    .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                l.LastActivityUtc.AddHours(nextEmailDelayHours),
                (l.Stage == LeadStage.Cold
                    ? LeadStage.Warm
                    : l.Stage == LeadStage.Warm
                        ? LeadStage.Mql
                        : LeadStage.Hot).ToString(),
                l.SignupCompleted,
                l.ProfileCompletion,
                l.SelectedPlan,
                l.PlanRenewalDate))
            .ToListAsync();

        leads = await ApplyCampaignMetadataFallbackAsync(db, leads);

        var eventsByType = await db.Events
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(k => k.Type, v => v.Count);

        var stageCounts = leads
            .GroupBy(l => l.Stage)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            totalLeads = leads.Count,
            stageCounts,
            eventsByType,
            leads
        });
    }

    private static async Task<List<LeadDashboardDto>> ApplyCampaignMetadataFallbackAsync(
        LeadScoringDbContext db,
        List<LeadDashboardDto> leads)
    {
        var needs = leads.Where(x => string.IsNullOrWhiteSpace(x.LastEventCampaign)).Select(x => x.Id).ToHashSet();
        if (needs.Count == 0)
        {
            return leads;
        }

        var events = await db.Events.AsNoTracking()
            .Where(e => e.LeadId != null && needs.Contains(e.LeadId.Value))
            .Select(e => new { e.LeadId, e.MetadataJson, e.TimestampUtc })
            .ToListAsync();

        var byLead = events
            .Where(e => e.LeadId.HasValue)
            .GroupBy(e => e.LeadId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.TimestampUtc).ToList());

        return leads.Select(l =>
        {
            if (!string.IsNullOrWhiteSpace(l.LastEventCampaign))
            {
                return l;
            }

            if (!byLead.TryGetValue(l.Id, out var evs))
            {
                return l;
            }

            foreach (var ev in evs)
            {
                var c = EventCampaignResolver.FromMetadata(ev.MetadataJson);
                if (!string.IsNullOrWhiteSpace(c))
                {
                    return l with { LastEventCampaign = c };
                }
            }

            return l;
        }).ToList();
    }
}
