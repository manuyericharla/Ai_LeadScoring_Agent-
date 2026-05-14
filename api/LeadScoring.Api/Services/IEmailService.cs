namespace LeadScoring.Api.Services;

public interface IEmailService
{
    /// <param name="suppressObserverBcc">When true, do not add <c>Email:AlwaysBcc</c> recipients (e.g. follow-up mails).</param>
    Task SendAsync(string to, string subject, string htmlBody, bool suppressObserverBcc = false);
}
