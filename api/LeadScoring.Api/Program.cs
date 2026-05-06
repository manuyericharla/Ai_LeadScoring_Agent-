using LeadScoring.Api.Background;
using LeadScoring.Api.Data;
using LeadScoring.Api.Repositories;
using LeadScoring.Api.Services;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<LeadScoringDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Hiperbrains")
                   ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing."));
    opt.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<LeadScoringService>();
builder.Services.AddScoped<VisitorAttributionService>();
builder.Services.AddScoped<LeadImportService>();
builder.Services.AddScoped<IFollowUpSubjectGenerator, OpenAiFollowUpSubjectGenerator>();
builder.Services.AddHttpClient(nameof(OpenAiFollowUpSubjectGenerator), client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IBatchRepository, BatchRepository>();
builder.Services.AddScoped<IBatchProcessingService, BatchProcessingService>();
builder.Services.AddHttpClient(nameof(UserSignupStatusService), client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("TrackingClickProbe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddScoped<IUserSignupStatusService, UserSignupStatusService>();
var disableEmailSending = builder.Configuration.GetValue<bool>("Email:DisableSending");
if (disableEmailSending)
{
    builder.Services.AddScoped<IEmailService, ConsoleEmailService>();
}
else
{
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
}
builder.Services.AddHostedService<InactivityWorker>();
builder.Services.AddHostedService<BatchWorker>();
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var allowedCorsOrigins = new HashSet<string>(configuredCorsOrigins, StringComparer.OrdinalIgnoreCase);
// Default policy so endpoint routing applies CORS (incl. OPTIONS preflight) with credentials for the SPA.
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(p => p
        .SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                return false;
            }

            if (allowedCorsOrigins.Contains(origin))
            {
                return true;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1")
            {
                return true;
            }

            return IsPrivateNetworkHost(uri.Host);
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

app.UseRouting();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/", () => Results.Redirect("/swagger"));
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Email:EnableObserverTestEndpoint"))
{
    app.MapPost("/internal/email-observer-test", async (IEmailService email) =>
    {
        const string sampleTo = "prathap.scadea@gmail.com";
        var html = """
            <html><body>
            <p>LeadScoring.Api observer test — same <code>IEmailService</code> path as batch and inactivity schedulers.</p>
            <p>With <code>Email:AlwaysBcc</code>, real sends include Bcc to the observer unless it matches the lead address.</p>
            </body></html>
            """;
        await email.SendAsync(sampleTo, "[LeadScoring] Observer / scheduler pipeline test", html);
        return Results.Ok(new
        {
            status = "sent",
            to = sampleTo,
            note = "One copy when To equals observer; otherwise lead gets To and observer gets Bcc."
        });
    });
}

app.MapControllers();

app.Run();

static bool IsPrivateNetworkHost(string host)
{
    if (!IPAddress.TryParse(host, out var ip))
    {
        return false;
    }

    var bytes = ip.GetAddressBytes();
    return bytes.Length == 4 &&
           (
               bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168)
           );
}
