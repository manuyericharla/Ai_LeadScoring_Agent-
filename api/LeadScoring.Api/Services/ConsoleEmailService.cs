namespace LeadScoring.Api.Services;

public class ConsoleEmailService(IConfiguration configuration, ILogger<ConsoleEmailService> logger) : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody, bool suppressObserverBcc = false)
    {
        var observers = suppressObserverBcc ? [] : EmailAlwaysBcc.GetAddresses(configuration);
        var bccNote = observers.Count == 0
            ? string.Empty
            : $" | Bcc (would also go to): {string.Join(", ", observers.Where(o => !string.Equals(o, to, StringComparison.OrdinalIgnoreCase)))}";
        logger.LogInformation("Email -> {To} | Subject: {Subject}{BccNote} | Body: {HtmlBody}", to, subject, bccNote, htmlBody);
        return Task.CompletedTask;
    }
}
