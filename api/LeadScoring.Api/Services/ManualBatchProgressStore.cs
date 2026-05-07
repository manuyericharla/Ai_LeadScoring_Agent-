using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using System.Collections.Concurrent;

namespace LeadScoring.Api.Services;

public class ManualBatchProgressStore
{
    private readonly ConcurrentDictionary<Guid, ManualBatchProgressState> jobs = new();

    public ManualBatchProgressState CreateJob(CampaignBatchType batchType, string scope, int totalLeads)
    {
        var state = new ManualBatchProgressState(
            Guid.NewGuid(),
            batchType,
            scope,
            true,
            totalLeads,
            0,
            0,
            0,
            null);
        jobs[state.JobId] = state;
        return state;
    }

    public BatchManualRunStatusDto? GetStatus(Guid jobId)
    {
        if (!jobs.TryGetValue(jobId, out var state))
        {
            return null;
        }

        return new BatchManualRunStatusDto(
            state.JobId,
            state.BatchType,
            state.Scope,
            state.IsRunning,
            state.TotalLeads,
            state.ProcessedCount,
            state.SuccessCount,
            state.FailureCount,
            state.Result);
    }

    public void IncrementProcessed(Guid jobId, bool success)
    {
        if (!jobs.TryGetValue(jobId, out var current))
        {
            return;
        }

        var next = current with
        {
            ProcessedCount = current.ProcessedCount + 1,
            SuccessCount = current.SuccessCount + (success ? 1 : 0),
            FailureCount = current.FailureCount + (success ? 0 : 1)
        };
        jobs[jobId] = next;
    }

    public void Complete(Guid jobId, BatchManualRunResultDto result)
    {
        if (!jobs.TryGetValue(jobId, out var current))
        {
            return;
        }

        jobs[jobId] = current with
        {
            IsRunning = false,
            ProcessedCount = result.TotalLeads,
            SuccessCount = result.SuccessCount,
            FailureCount = result.FailureCount,
            Result = result
        };
    }

    public void Fail(Guid jobId)
    {
        if (!jobs.TryGetValue(jobId, out var current))
        {
            return;
        }

        jobs[jobId] = current with { IsRunning = false };
    }

    public readonly record struct ManualBatchProgressState(
        Guid JobId,
        CampaignBatchType BatchType,
        string Scope,
        bool IsRunning,
        int TotalLeads,
        int ProcessedCount,
        int SuccessCount,
        int FailureCount,
        BatchManualRunResultDto? Result);
}
