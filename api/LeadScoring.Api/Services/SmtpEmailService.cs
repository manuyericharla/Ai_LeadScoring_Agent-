using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace LeadScoring.Api.Services;

public class SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        // For local testing, prefer values from appsettings.json explicitly.
        // This avoids accidental overrides from machine-level environment variables.
        var appSettingsPathConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var smtpSection = appSettingsPathConfig.GetSection("SMTP");
        var emailSection = appSettingsPathConfig.GetSection("Email");

        var host = smtpSection["Host"] ?? configuration["SMTP:Host"] ?? throw new InvalidOperationException("SMTP:Host is missing.");
        var portValue = smtpSection["Port"] ?? configuration["SMTP:Port"] ?? throw new InvalidOperationException("SMTP:Port is missing.");
        var username = smtpSection["Username"] ?? configuration["SMTP:Username"] ?? throw new InvalidOperationException("SMTP:Username is missing.");
        var password = smtpSection["Password"] ?? configuration["SMTP:Password"] ?? throw new InvalidOperationException("SMTP:Password is missing.");
        var fromAddress = emailSection["FromAddress"] ?? configuration["Email:FromAddress"] ?? throw new InvalidOperationException("Email:FromAddress is missing.");

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
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password)
        };

        await client.SendMailAsync(message);
        logger.LogInformation("Email sent to {Recipient}{BccLog}", to, message.Bcc.Count > 0 ? $" with {message.Bcc.Count} observer Bcc" : string.Empty);
    }
}
