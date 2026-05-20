using LeadScoring.Api;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(
    ICompanyLeadDbAccessor companyLeadDb,
    ITenantContext tenantContext,
    ITenantLeadScope tenantLeadScope) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        tenantContext.RequireTenant();
        const int nextEmailDelayHours = 24;

        await tenantLeadScope.EnsureTenantContextMatchesUserAsync(cancellationToken);
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken);
        var db = companyLeadDb.GetDbContext();
        var scopedLeads = tenantLeadScope.ApplyScope(db.Leads, companyName);

        var leads = await (
            from l in scopedLeads
            let lastEv = db.Events
                .Where(e => e.LeadId == l.Id)
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefault()
            let lastNonEmptyCampaign = db.Events
                .Where(e => e.LeadId == l.Id && e.Campaign != null && e.Campaign != "")
                .OrderByDescending(e => e.TimestampUtc)
                .Select(e => e.Campaign)
                .FirstOrDefault()
            let eventScoreSum = db.Events
                .Where(e => e.LeadId == l.Id)
                .Sum(e => e.EventScore)
            let src = lastEv != null ? (EventSource?)lastEv.Source : l.LastSource
            orderby l.LastActivityUtc descending
            select new LeadDashboardDto(
                l.Id,
                l.Email,
                eventScoreSum,
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
            .ToListAsync(cancellationToken);

        leads = await ApplyCampaignMetadataFallbackAsync(db, leads);

        var scopedLeadIds = scopedLeads.Select(l => l.Id);

        var eventsByType = await db.Events
            .Where(e => e.LeadId != null && scopedLeadIds.Contains(e.LeadId.Value))
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(k => k.Type, v => v.Count, cancellationToken);

        var stageCounts = leads
            .GroupBy(l => l.Stage)
            .ToDictionary(g => g.Key, g => g.Count());

        var signedUpCount = leads.Count(x => x.SignupCompleted);
        var firstSourceCounts = await scopedLeads
            .AsNoTracking()
            .GroupBy(l => l.FirstSource)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var firstSourceBuckets = firstSourceCounts
            .GroupBy(x => (x.Source ?? EventSource.Unknown).ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.Count),
                StringComparer.OrdinalIgnoreCase);

        return Ok(new
        {
            companyName,
            totalLeads = leads.Count,
            signedUpCount,
            stageCounts,
            eventsByType,
            firstSourceCounts = firstSourceBuckets,
            leads
        });
    }

    private static async Task<List<LeadDashboardDto>> ApplyCampaignMetadataFallbackAsync(
        PublicCompanyDbContext db,
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
