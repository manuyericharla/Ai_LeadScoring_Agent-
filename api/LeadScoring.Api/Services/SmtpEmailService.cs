using System.Net;
using System.Net.Mail;

namespace LeadScoring.Api.Services;

public class SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var smtpSection = configuration.GetSection("SMTP");
        var emailSection = configuration.GetSection("Email");

        var host = smtpSection["Host"] ?? throw new InvalidOperationException("SMTP:Host is missing.");
        var portValue = smtpSection["Port"] ?? throw new InvalidOperationException("SMTP:Port is missing.");
        var username = smtpSection["Username"] ?? throw new InvalidOperationException("SMTP:Username is missing.");
        var password = smtpSection["Password"] ?? throw new InvalidOperationException("SMTP:Password is missing.");
        var fromAddress = emailSection["FromAddress"] ?? throw new InvalidOperationException("Email:FromAddress is missing.");

        if (!int.TryParse(portValue, out var port))
        {
            throw new InvalidOperationException("SMTP:Port must be a valid integer.");
        }

        using var message = new MailMessage(fromAddress, to, subject, htmlBody) { IsBodyHtml = true };
        var observers = EmailAlwaysBcc.GetAddresses(configuration);
        EmailAlwaysBcc.AddDistinctBcc(message.Bcc, to, observers);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };

        await client.SendMailAsync(message);
        logger.LogInformation("Email sent to {Recipient}{BccLog}", to, message.Bcc.Count > 0 ? $" with {message.Bcc.Count} observer Bcc" : string.Empty);
    }
}
