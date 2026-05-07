using LeadScoring.Api.Services;

namespace LeadScoring.Api.Background;

public class BatchWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = configuration["BatchProcessing:DailyRunTimeUtc"] ?? "00:30";
        if (!TimeSpan.TryParse(schedule, out var runAtUtc))
        {
            runAtUtc = new TimeSpan(0, 30, 0);
        }

        logger.LogInformation("Batch worker started. DailyRunTimeUtc={DailyRunTimeUtc}.", runAtUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;
            var nextRunUtc = nowUtc.Date.Add(runAtUtc);
            if (nextRunUtc <= nowUtc)
            {
                nextRunUtc = nextRunUtc.AddDays(1);
            }

            var delay = nextRunUtc - nowUtc;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var batchService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                await batchService.ProcessActiveConfigsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Batch worker cycle failed.");
            }
        }
    }
}
