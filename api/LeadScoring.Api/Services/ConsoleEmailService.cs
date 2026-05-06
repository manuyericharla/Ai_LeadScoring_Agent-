namespace LeadScoring.Api.Services;

public class ConsoleEmailService(IConfiguration configuration, ILogger<ConsoleEmailService> logger) : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody)
    {
        var observers = EmailAlwaysBcc.GetAddresses(configuration);
        var bccNote = observers.Count == 0
            ? string.Empty
            : $" | Bcc (would also go to): {string.Join(", ", observers.Where(o => !string.Equals(o, to, StringComparison.OrdinalIgnoreCase)))}";
        logger.LogInformation("Email -> {To} | Subject: {Subject}{BccNote} | Body: {HtmlBody}", to, subject, bccNote, htmlBody);
        return Task.CompletedTask;
    }
}
