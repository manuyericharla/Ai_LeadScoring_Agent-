using System.Text;
using LeadScoring.Api.Background;
using LeadScoring.Api.Data;
using LeadScoring.Api.Repositories;
using LeadScoring.Api.Services;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

var masterConnectionString = builder.Configuration.GetConnectionString("Hiperbrains")
    ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

builder.Services.AddDbContext<MasterDbContext>(opt =>
{
    opt.UseNpgsql(masterConnectionString, npg =>
        npg.MigrationsHistoryTable("__MasterMigrationsHistory"));
    opt.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddScoped<ITenantDbContextAccessor, TenantDbContextAccessor>();
builder.Services.AddScoped<LeadScoringDbContext>(sp =>
    sp.GetRequiredService<ITenantDbContextAccessor>().GetDbContext());

builder.Services.AddScoped<ITenantDatabaseProvisioner, TenantDatabaseProvisioner>();
builder.Services.AddScoped<JwtAuthTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var jwtSigningKey = builder.Configuration["Auth:JwtSigningKey"]
    ?? builder.Configuration["Tracking:SigningKey"]
    ?? throw new InvalidOperationException("Auth:JwtSigningKey or Tracking:SigningKey is required.");
var jwtIssuer = builder.Configuration["Auth:JwtIssuer"] ?? "LeadScoring";
var jwtAudience = builder.Configuration["Auth:JwtAudience"] ?? "LeadScoring.App";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();
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
builder.Services.AddSingleton<ManualBatchProgressStore>();
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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
    await masterDb.Database.MigrateAsync();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

var configuredEmailImagesPath = app.Configuration["PublicAssets:EmailImagesPhysicalPath"];
var emailImagesSourcePath = ResolveEmailImagesSourcePath(configuredEmailImagesPath, app.Environment.ContentRootPath);

var emailImagesRequestPath = app.Configuration["PublicAssets:EmailImagesRequestPath"];
if (string.IsNullOrWhiteSpace(emailImagesRequestPath))
{
    emailImagesRequestPath = "/assets/images";
}
if (!emailImagesRequestPath.StartsWith('/'))
{
    emailImagesRequestPath = $"/{emailImagesRequestPath}";
}

if (Directory.Exists(emailImagesSourcePath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(emailImagesSourcePath),
        RequestPath = emailImagesRequestPath,
        OnPrepareResponse = ctx =>
        {
            // Public email assets: allow any origin to fetch images.
            ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Context.Response.Headers["Access-Control-Allow-Methods"] = "GET,HEAD,OPTIONS";
            ctx.Context.Response.Headers["Access-Control-Allow-Headers"] = "*";
            ctx.Context.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            ctx.Context.Response.Headers["Timing-Allow-Origin"] = "*";
            ctx.Context.Response.Headers.Remove("Content-Security-Policy");
            ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=31536000,immutable";
            ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        }
    });
}
else
{
    app.Logger.LogWarning("Email image static path not found. Configured path: {ConfiguredPath}. Resolved path: {ResolvedPath}",
        configuredEmailImagesPath,
        emailImagesSourcePath);
}

// Authenticated JSON endpoints must not be cached by browsers/CDNs/reverse proxies.
// Without this, conditional GETs (If-None-Match / If-Modified-Since) can return 304 with no
// body, which Angular HttpClient surfaces as a load failure (e.g. "Could not load ...").
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/health", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(emailImagesRequestPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
        }
        return Task.CompletedTask;
    });
    await next();
});

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

static string ResolveEmailImagesSourcePath(string? configuredPath, string contentRootPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        var explicitPath = Path.GetFullPath(configuredPath);
        if (Directory.Exists(explicitPath))
        {
            return explicitPath;
        }
    }

    var candidatePaths = new[]
    {
        Path.Combine(contentRootPath, "..", "..", "ui", "src", "assets", "images"),
        Path.Combine(contentRootPath, "..", "..", "..", "..", "ui", "src", "assets", "images"),
        Path.Combine(contentRootPath, "ui", "src", "assets", "images"),
        Path.Combine(Directory.GetCurrentDirectory(), "ui", "src", "assets", "images"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "ui", "src", "assets", "images")
    };

    foreach (var candidate in candidatePaths)
    {
        var fullPath = Path.GetFullPath(candidate);
        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }
    }

    return Path.GetFullPath(candidatePaths[0]);
}
