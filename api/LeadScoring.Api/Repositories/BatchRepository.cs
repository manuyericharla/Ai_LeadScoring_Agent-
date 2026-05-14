using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Repositories;

public class BatchRepository(LeadScoringDbContext db) : IBatchRepository
{
    public Task<bool> HasBatchRunOnDateAsync(DateTime runDateUtc, CancellationToken cancellationToken)
    {
        var fromUtc = runDateUtc.Date;
        var toUtc = fromUtc.AddDays(1);
        return db.BatchLogs.AnyAsync(x => x.RunDate >= fromUtc && x.RunDate < toUtc, cancellationToken);
    }

    public async Task<CampaignBatchType?> GetLastCompletedDailyBatchTypeAsync(CancellationToken cancellationToken)
    {
        return await db.BatchLogs
            .OrderByDescending(x => x.RunDate)
            .Select(x => (CampaignBatchType?)x.BatchType)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<Lead>> GetDay1LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken)
    {
        var fromUtc = runDateUtc.Date;
        var toUtc = fromUtc.AddDays(1);
        return db.Leads
            .Where(x =>
                x.Stage == LeadStage.Cold &&
                x.CreatedAtUtc >= fromUtc &&
                x.CreatedAtUtc < toUtc &&
                !x.WelcomeEmailSent &&
                (x.NextEmailSendDateUtc == null || x.NextEmailSendDateUtc <= runDateUtc))
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Lead>> GetDay2LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken)
    {
        var inactivityThresholdUtc = runDateUtc.AddDays(-2);
        return db.Leads
            .Where(x =>
                x.WelcomeEmailSent &&
                x.LastActivityUtc <= inactivityThresholdUtc &&
                (x.LastEmailSentDateUtc == null || x.LastEmailSentDateUtc <= inactivityThresholdUtc) &&
                (x.NextEmailSendDateUtc == null || x.NextEmailSendDateUtc <= runDateUtc))
            .OrderBy(x => x.LastActivityUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Lead>> GetDay3LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken)
    {
        var fromUtc = runDateUtc.Date;
        var toUtc = fromUtc.AddDays(1);
        return db.Leads
            .Where(x =>
                ((x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc) ||
                 x.Stage == LeadStage.Mql ||
                 x.Stage == LeadStage.Hot) &&
                (x.LastEmailSentDateUtc == null || x.LastEmailSentDateUtc < fromUtc) &&
                (x.NextEmailSendDateUtc == null || x.NextEmailSendDateUtc <= runDateUtc))
            .OrderBy(x => x.LastEmailSentDateUtc ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Lead>> GetDay4LeadsAsync(DateTime runDateUtc, CancellationToken cancellationToken)
    {
        var thresholdUtc = runDateUtc.AddDays(-4);
        return db.Leads
            .Where(x =>
                x.LastEmailSentDateUtc.HasValue &&
                x.LastEmailSentDateUtc <= thresholdUtc &&
                (x.NextEmailSendDateUtc == null || x.NextEmailSendDateUtc <= runDateUtc))
            .OrderBy(x => x.LastEmailSentDateUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Lead>> GetAllLeadsForPreviewAsync(CancellationToken cancellationToken)
    {
        return db.Leads
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<BatchPreviewLeadAggregates> GetLeadAggregatesForPreviewAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var fromNew = nowUtc.AddDays(-1);
        var inactiveThresholdUtc = nowUtc.AddDays(-2);
        var emailThresholdUtc = nowUtc.AddDays(-4);
        var baseLeads = db.Leads.AsNoTracking();

        // Do not run these concurrently: one scoped DbContext cannot execute overlapping operations.
        var total = await baseLeads.CountAsync(cancellationToken).ConfigureAwait(false);
        var s0 = await baseLeads.CountAsync(l => l.Stage == LeadStage.Cold, cancellationToken).ConfigureAwait(false);
        var s1 = await baseLeads.CountAsync(l => l.Stage == LeadStage.Warm, cancellationToken).ConfigureAwait(false);
        var s2 = await baseLeads.CountAsync(l => l.Stage == LeadStage.Mql, cancellationToken).ConfigureAwait(false);
        var s3 = await baseLeads.CountAsync(l => l.Stage == LeadStage.Hot, cancellationToken).ConfigureAwait(false);
        var newLeads = await baseLeads.CountAsync(l => l.CreatedAtUtc >= fromNew, cancellationToken).ConfigureAwait(false);
        var inactive = await baseLeads.CountAsync(l => l.LastActivityUtc <= inactiveThresholdUtc, cancellationToken).ConfigureAwait(false);
        var emailGap = await baseLeads.CountAsync(
            l => l.LastEmailSentDateUtc.HasValue && l.LastEmailSentDateUtc <= emailThresholdUtc,
            cancellationToken).ConfigureAwait(false);
        var didNotOpen = await LeadsWithNoUserEngagementSinceLastEmail().CountAsync(cancellationToken).ConfigureAwait(false);

        return new BatchPreviewLeadAggregates(
            total,
            s0,
            s1,
            s2,
            s3,
            total - s0 - s1 - s2 - s3,
            newLeads,
            inactive,
            emailGap,
            didNotOpen);
    }

    public Task<List<Lead>> GetLeadsDidNotOpenSinceLastEmailAsync(CancellationToken cancellationToken)
    {
        return LeadsWithNoUserEngagementSinceLastEmail().ToListAsync(cancellationToken);
    }

    private IQueryable<Lead> LeadsWithNoUserEngagementSinceLastEmail()
    {
        const string systemMarkerFragment = "\"systemMarker\":";
        return db.Leads
            .AsNoTracking()
            .Where(l => l.LastEmailSentDateUtc != null)
            .Where(l => !db.Events.Any(e =>
                e.LeadId == l.Id &&
                e.TimestampUtc > l.LastEmailSentDateUtc!.Value &&
                (e.MetadataJson == null || !e.MetadataJson.Contains(systemMarkerFragment))));
    }

    public Task<bool> HasBatchMarkerEventAsync(Guid leadId, string marker, DateTime fromUtc, CancellationToken cancellationToken)
    {
        return db.Events.AnyAsync(x =>
            x.LeadId == leadId &&
            x.MetadataJson != null &&
            x.MetadataJson.Contains($"\"systemMarker\":\"{marker}\""), cancellationToken);
    }

    public Task<bool> HasEngagementSinceLastEmailAsync(Guid leadId, DateTime lastEmailSentUtc, CancellationToken cancellationToken)
    {
        return db.Events.AnyAsync(x =>
            x.LeadId == leadId &&
            x.TimestampUtc > lastEmailSentUtc &&
            (x.MetadataJson == null || !x.MetadataJson.Contains("\"systemMarker\":")), cancellationToken);
    }

    public Task<EmailTemplate?> GetTemplateByBatchTypeAsync(CampaignBatchType batchType, Lead lead, CancellationToken cancellationToken)
    {
        return batchType switch
        {
            CampaignBatchType.Day1 => db.EmailTemplates
                .Where(t => t.IsActive && !t.IsFollowUp && t.Stage == LeadStage.Cold && (t.ProductId == lead.ProductId || t.ProductId == null))
                .OrderByDescending(t => t.ProductId == lead.ProductId)
                .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken),
            CampaignBatchType.Day2 => db.EmailTemplates
                .Where(t => t.IsActive && t.IsFollowUp && t.Stage == LeadStage.Cold && (t.ProductId == lead.ProductId || t.ProductId == null))
                .OrderByDescending(t => t.ProductId == lead.ProductId)
                .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken),
            CampaignBatchType.Day3 => db.EmailTemplates
                .Where(t => t.IsActive && !t.IsFollowUp && t.Stage == lead.Stage && (t.ProductId == lead.ProductId || t.ProductId == null))
                .OrderByDescending(t => t.ProductId == lead.ProductId)
                .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken),
            CampaignBatchType.Day4 => db.EmailTemplates
                .Where(t => t.IsActive && t.IsFollowUp && (t.Stage == LeadStage.Mql || t.Stage == LeadStage.Hot) && (t.ProductId == lead.ProductId || t.ProductId == null))
                .OrderByDescending(t => t.ProductId == lead.ProductId)
                .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken),
            _ => Task.FromResult<EmailTemplate?>(null)
        };
    }

    public async Task<BatchLog> CreateBatchLogAsync(BatchLog batchLog, CancellationToken cancellationToken)
    {
        await db.BatchLogs.AddAsync(batchLog, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return batchLog;
    }

    public Task<List<BatchLog>> GetRecentBatchLogsAsync(int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 500);
        return db.BatchLogs
            .AsNoTracking()
            .OrderByDescending(x => x.BatchId)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminBatchReport> UpsertAdminReportAsync(
        string email,
        int stage0Count,
        int stage1Count,
        int stage2Count,
        int stage3Count,
        int stage4Count,
        CancellationToken cancellationToken)
    {
        var entity = await db.AdminBatchReports.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        var utcNow = DateTime.UtcNow;
        if (entity is null)
        {
            entity = new AdminBatchReport
            {
                Email = email,
                CreatedDate = utcNow
            };
            await db.AdminBatchReports.AddAsync(entity, cancellationToken);
        }

        entity.UpdatedDate = utcNow;
        entity.Stage0Count = stage0Count;
        entity.Stage1Count = stage1Count;
        entity.Stage2Count = stage2Count;
        entity.Stage3Count = stage3Count;
        entity.Stage4Count = stage4Count;
        entity.BatchDailyCount++;

        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public Task<List<string>> GetAdminReportEmailsAsync(CancellationToken cancellationToken)
    {
        return db.AdminBatchReports
            .AsNoTracking()
            .Where(x => x.Email != null && x.Email != string.Empty)
            .Select(x => x.Email)
            .ToListAsync(cancellationToken);
    }

    public Task<List<BatchConfig>> GetActiveConfigsAsync(CancellationToken cancellationToken)
    {
        return db.BatchConfigs
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
    }

    public Task<Batch?> GetBatchByIdAsync(long batchId, CancellationToken cancellationToken)
    {
        return db.Batches
            .Include(x => x.BatchLeads)
            .FirstOrDefaultAsync(x => x.BatchId == batchId, cancellationToken);
    }

    public Task<List<Lead>> GetLeadsAfterAsync(int productId, LeadStage stage, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        return db.Leads
            .AsNoTracking()
            .Where(x =>
                x.ProductId == productId &&
                x.Stage == stage &&
                (
                    x.CreatedAtUtc > sinceUtc ||
                    (x.LastScoredAtUtc.HasValue && x.LastScoredAtUtc.Value > sinceUtc)
                ))
            .OrderBy(x => x.LastScoredAtUtc ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Batch> CreateBatchAsync(Batch batch, CancellationToken cancellationToken)
    {
        await db.Batches.AddAsync(batch, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return batch;
    }

    public async Task CreateBatchLeadsAsync(IEnumerable<BatchLead> batchLeads, CancellationToken cancellationToken)
    {
        await db.BatchLeads.AddRangeAsync(batchLeads, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<List<BatchLead>> GetFailedBatchLeadsAsync(long batchId, CancellationToken cancellationToken)
    {
        return db.BatchLeads
            .Where(x => x.BatchId == batchId && x.Status == BatchLeadStatus.Failed)
            .ToListAsync(cancellationToken);
    }

    public Task<Lead?> GetLeadForUpdateAsync(Guid leadId, CancellationToken cancellationToken)
    {
        return db.Leads.FirstOrDefaultAsync(x => x.Id == leadId, cancellationToken);
    }

    public Task<Lead?> GetLeadByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<Lead?>(null);
        }

        var e = email.Trim().ToLowerInvariant();
        return db.Leads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email.ToLower() == e, cancellationToken);
    }

    public Task<int?> GetAnyLeadProductIdAsync(CancellationToken cancellationToken)
    {
        return db.Leads
            .AsNoTracking()
            .Where(l => l.ProductId != null)
            .Select(l => l.ProductId)
            .FirstOrDefaultAsync(cancellationToken);
    }


    public Task<EmailTemplate?> GetActiveTemplateForStageAsync(LeadStage stage, int? productId, CancellationToken cancellationToken)
    {
        return db.EmailTemplates
            .Where(t =>
                t.IsActive &&
                !t.IsFollowUp &&
                !EF.Functions.ILike(t.Name, "%dummy%") &&
                t.Stage == stage &&
                (t.ProductId == productId || t.ProductId == null))
            .OrderByDescending(t => t.ProductId == productId)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddEventAsync(LeadEvent leadEvent, CancellationToken cancellationToken)
    {
        await db.Events.AddAsync(leadEvent, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}
