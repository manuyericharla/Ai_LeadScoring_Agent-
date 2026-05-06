using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Repositories;

public class BatchRepository(LeadScoringDbContext db) : IBatchRepository
{
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
