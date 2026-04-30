using LeadScoring.Api.Contracts;
using System.Text.Json;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("track")]
public class TrackingController(
    TokenService tokenService,
    IHttpClientFactory httpClientFactory,
    ILogger<TrackingController> logger,
    LeadScoringService scoringService,
    VisitorAttributionService visitorAttributionService,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("/r")]
    public async Task<IActionResult> UniversalTrackingLink([FromQuery] string? src, [FromQuery] string? cmp, [FromQuery] string? redirect)
    {
        var source = VisitorAttributionService.ParseSource(src);
        var visitorId = GetOrCreateVisitorId();
        var userAgent = Request.Headers.UserAgent.ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await visitorAttributionService.EnsureVisitorAsync(visitorId, source, userAgent, ipAddress);
        await visitorAttributionService.TrackAnonymousEventAsync(
            visitorId,
            source,
            EventType.WebsiteActivity,
            metadataJson: $$"""{"eventName":"UniversalTrackingClick","source":"{{source}}"}""",
            campaign: cmp);

        Response.Cookies.Append("visitorId", visitorId, new CookieOptions
        {
            HttpOnly = false,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        var destination = ResolveRedirect(redirect);
        return Redirect(destination);
    }

    [HttpGet("open")]
    public IActionResult TrackOpen([FromQuery] string token)
    {
        var leadId = tokenService.ValidateLeadToken(token);
        if (leadId is null)
        {
            return NotFound();
        }

        var pixel = Convert.FromBase64String("R0lGODlhAQABAPAAAAAAAAAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");
        return File(pixel, "image/gif");
    }

    [HttpGet("click")]
    public async Task<IActionResult> TrackClick([FromQuery] string token, [FromQuery] string redirect)
    {
        var leadId = tokenService.ValidateLeadToken(token);
        if (leadId is null || !Uri.IsWellFormedUriString(redirect, UriKind.Absolute))
        {
            return BadRequest("Invalid token or redirect URL.");
        }

        var probe = await ProbeRedirectStatusAsync(redirect);
        await scoringService.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = leadId.Value,
            Type = EventType.EmailClick,
            Source = EventSource.Email,
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new
            {
                redirect,
                eventName = "Email click",
                redirectStatusCode = probe.StatusCode,
                redirectSuccess = probe.IsSuccess,
                probeError = probe.Error
            })
        });

        return Redirect(redirect);
    }

    private async Task<(int? StatusCode, bool IsSuccess, string? Error)> ProbeRedirectStatusAsync(string redirect)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, redirect);
            var client = httpClientFactory.CreateClient("TrackingClickProbe");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var statusCode = (int)response.StatusCode;
            var isSuccess = response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.RedirectKeepVerb or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
            return (statusCode, isSuccess, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tracking redirect probe failed for {Redirect}", redirect);
            return (null, false, ex.Message);
        }
    }

    [HttpPost("event")]
    public async Task<IActionResult> TrackEvent([FromBody] TrackEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VisitorId))
        {
            return BadRequest("visitorId is required.");
        }

        var eventType = Enum.TryParse<EventType>(request.EventType, true, out var parsed) ? parsed : EventType.WebsiteActivity;
        var metadataJson = MergeMetadata(request.MetadataJson, request.EventType);
        var source = VisitorAttributionService.ParseSource(request.Source);

        await visitorAttributionService.TrackAnonymousEventAsync(
            request.VisitorId,
            source,
            eventType,
            metadataJson,
            request.Campaign,
            request.LeadId);

        return Accepted();
    }

    private string ResolveRedirect(string? redirect)
    {
        if (!string.IsNullOrWhiteSpace(redirect) && Uri.IsWellFormedUriString(redirect, UriKind.Absolute))
        {
            return redirect;
        }

        return configuration["Tracking:DefaultRedirectUrl"] ?? "https://www.hiperlabs.com";
    }

    private string GetOrCreateVisitorId()
    {
        if (Request.Cookies.TryGetValue("visitorId", out var visitorId) && !string.IsNullOrWhiteSpace(visitorId))
        {
            return visitorId;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string? MergeMetadata(string? metadataJson, string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return metadataJson;
        }

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return $$"""{"eventName":"{{eventType}}"}""";
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return metadataJson;
            }

            var properties = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                properties.Add($"\"{property.Name}\":{property.Value.GetRawText()}");
            }

            if (!doc.RootElement.TryGetProperty("eventName", out _))
            {
                properties.Add($"\"eventName\":\"{eventType}\"");
            }

            return $"{{{string.Join(",", properties)}}}";
        }
        catch
        {
            return metadataJson;
        }
    }
}
