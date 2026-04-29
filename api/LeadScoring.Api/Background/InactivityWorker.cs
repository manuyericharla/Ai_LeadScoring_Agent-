using LeadScoring.Api.Services;

namespace LeadScoring.Api.Background;

public class InactivityWorker(IServiceScopeFactory scopeFactory, ILogger<InactivityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var followUpDelay = TimeSpan.FromHours(1);
        const int maxAttemptsPerDay = 3;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scoring = scope.ServiceProvider.GetRequiredService<LeadScoringService>();
                await scoring.CheckFirstEmailScoreUpdateAsync(followUpDelay);
                await scoring.RunWelcomeFollowUpSchedulerAsync(followUpDelay, maxAttemptsPerDay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hourly score-check worker failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
        }
    }
}
