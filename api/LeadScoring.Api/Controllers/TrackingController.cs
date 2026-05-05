using LeadScoring.Api.Contracts;
using System.Text.Json;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;

namespace LeadScoring.Api.Controllers;

/// <summary>Tracking, email gate, and merge endpoints used from the SPA on another origin — CORS metadata required.</summary>
[EnableCors]
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
        var validatedRedirect = ResolveSafeRedirectAbsolute(redirect);
        var source = VisitorAttributionService.ParseSource(src);
        var visitorId = GetOrCreateVisitorId();
        var userAgent = Request.Headers.UserAgent.ToString();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await visitorAttributionService.EnsureVisitorAsync(visitorId, source, userAgent, ipAddress);
        AppendVisitorCookie(visitorId);

        var metadata = new Dictionary<string, object?>
        {
            ["eventName"] = "UniversalTrackingClick",
            ["source"] = source.ToString(),
            ["visitorId"] = visitorId,
            ["redirectUrl"] = validatedRedirect.AbsoluteUri,
            ["funnelStage"] = "EmailGateQueued"
        };

        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer))
        {
            metadata["referer"] = referer;
        }

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            metadata["userAgent"] = userAgent.Length > 512 ? userAgent[..512] : userAgent;
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            metadata["ipAddress"] = ipAddress;
        }

        await visitorAttributionService.TrackAnonymousEventAsync(
            visitorId,
            source,
            EventType.WebsiteActivity,
            metadataJson: JsonSerializer.Serialize(metadata),
            campaign: cmp);

        var emailGateOrigin = RedirectSafety.BuildEmailGateUri(Request, configuration["Tracking:EmailGateOrigin"]);
        var emailBasePath = $"{emailGateOrigin.AbsoluteUri.TrimEnd('/')}/email";
        // Visitor id is kept in the first-party tracking cookie (and optional site script storage), not in the email-gate URL.
        var emailUrl = QueryHelpers.AddQueryString(emailBasePath, "src", VisitorAttributionService.ToAttributionToken(source));
        emailUrl = QueryHelpers.AddQueryString(emailUrl, "redirect", validatedRedirect.AbsoluteUri);
        if (!string.IsNullOrWhiteSpace(cmp))
        {
            emailUrl = QueryHelpers.AddQueryString(emailUrl, "cmp", cmp.Trim());
        }

        return Redirect(emailUrl);
    }

    /// <summary>Forwards /email on the API host to the email-gate SPA (same query string). Requires Tracking:EmailGateOrigin when API and UI differ.</summary>
    [HttpGet("/email")]
    public IActionResult ForwardEmailGateToSpa()
    {
        var emailGateOrigin = RedirectSafety.BuildEmailGateUri(Request, configuration["Tracking:EmailGateOrigin"]);
        var apiAuthority = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
        var gateAuthority = emailGateOrigin.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (string.Equals(apiAuthority, gateAuthority, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(new
            {
                error =
                    "Email capture is served by the SPA. Set Tracking:EmailGateOrigin to your UI base URL (for local dev see appsettings.Development.json)."
            });
        }

        var target = $"{emailGateOrigin.AbsoluteUri.TrimEnd('/')}/email{Request.QueryString}";
        return Redirect(target);
    }

    [HttpPost("/capture-email")]
    [Produces("application/json")]
    public async Task<IActionResult> CaptureEmail([FromBody] CaptureEmailRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
        {
            return BadRequest(new { error = "email is required." });
        }

        if (string.IsNullOrWhiteSpace(body.Redirect))
        {
            return BadRequest(new { error = "redirect is required." });
        }

        var visitorIdNormalized = TryResolveVisitorId(body.VisitorId);
        if (string.IsNullOrWhiteSpace(visitorIdNormalized))
        {
            visitorIdNormalized = Guid.NewGuid().ToString("N");
        }

        var trimmedEmail = body.Email.Trim();
        if (trimmedEmail.Length > 254 || !trimmedEmail.Contains('@', StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid email." });
        }

        var validatedTarget = ResolveSafeRedirectAbsolute(body.Redirect);
        var source = VisitorAttributionService.ParseSource(body.Source);
        var ua = Request.Headers.UserAgent.ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        await visitorAttributionService.EnsureVisitorAsync(visitorIdNormalized, source, ua, ip);

        await visitorAttributionService.CaptureEmailFromGateAsync(
            visitorIdNormalized,
            trimmedEmail,
            source,
            body.Campaign,
            body.DwellMs);

        AppendVisitorCookie(visitorIdNormalized);

        var merged = MergeAttributionIntoRedirect(
            validatedTarget.AbsoluteUri,
            source,
            body.Campaign,
            appendEmailCaptured: true,
            capturedEmail: trimmedEmail,
            attributionSourceRaw: body.Source);
        return Ok(new CaptureEmailResponse(merged, visitorIdNormalized));
    }

    [HttpPost("/skip-email-gate")]
    [Produces("application/json")]
    public async Task<IActionResult> SkipEmailGate([FromBody] SkipEmailGateRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Redirect))
        {
            return BadRequest(new { error = "redirect is required." });
        }

        var visitorIdNormalized = TryResolveVisitorId(body.VisitorId);
        if (string.IsNullOrWhiteSpace(visitorIdNormalized))
        {
            visitorIdNormalized = Guid.NewGuid().ToString("N");
        }

        var validatedTarget = ResolveSafeRedirectAbsolute(body.Redirect);
        var source = VisitorAttributionService.ParseSource(body.Source);
        var ua = Request.Headers.UserAgent.ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        await visitorAttributionService.EnsureVisitorAsync(visitorIdNormalized, source, ua, ip);

        var skipMetadata = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["eventName"] = "EmailGateSkipped",
            ["visitorId"] = visitorIdNormalized,
            ["declaredEmail"] = "unknown",
            ["dwellMs"] = body.DwellMs
        });

        await visitorAttributionService.TrackAnonymousEventAsync(
            visitorIdNormalized,
            source,
            EventType.WebsiteActivity,
            metadataJson: skipMetadata,
            body.Campaign);

        AppendVisitorCookie(visitorIdNormalized);

        // Landing URL: only source, email=unknown, campaign — do not carry leadId or other params from redirect.
        var merged = MergeAttributionIntoRedirect(
            validatedTarget.AbsoluteUri,
            source,
            body.Campaign,
            appendEmailCaptured: false,
            capturedEmail: null,
            attributionSourceRaw: body.Source,
            unknownEmailSkipMinimal: true);
        return Ok(new CaptureEmailResponse(merged, visitorIdNormalized));
    }

    [HttpGet("email-hint")]
    [Produces("application/json")]
    public async Task<IActionResult> EmailHint([FromQuery] string? visitorId)
    {
        var resolved = TryResolveVisitorId(visitorId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            // Empty payload so the SPA can prefill from localStorage / continue without a 400 in DevTools.
            return Ok(new VisitorEmailHintResponse(null, false, null));
        }

        var already = await visitorAttributionService.VisitorAlreadyIdentifiedAsync(resolved);
        var email = await visitorAttributionService.TryGetCapturedEmailAsync(resolved);
        return Ok(new VisitorEmailHintResponse(email, already, resolved));
    }

    /// <summary>Build allowlisted attribution redirect for the SPA.</summary>
    [HttpGet("merged-destination")]
    [Produces("application/json")]
    public IActionResult MergedDestination(
        [FromQuery] string? redirect,
        [FromQuery] string? src,
        [FromQuery] string? cmp,
        [FromQuery] bool emailCaptured = false)
    {
        var validated = ResolveSafeRedirectAbsolute(redirect);
        var sourceParsed = VisitorAttributionService.ParseSource(src);
        var merged = MergeAttributionIntoRedirect(
            validated.AbsoluteUri,
            sourceParsed,
            cmp,
            appendEmailCaptured: emailCaptured,
            capturedEmail: null,
            attributionSourceRaw: src);
        return Ok(new RedirectMergeResponse(merged));
    }


    /// <summary>
    /// Cross-origin redirects cannot rely on the tracking host cookie. Merges attribution into the destination query,
    /// including <c>source</c>, <c>email</c> (after capture), and <c>campaign</c> for landing apps, plus legacy
    /// <c>ls_src</c> / <c>utm_source</c> / <c>utm_campaign</c> where applicable.
    /// When <paramref name="unknownEmailSkipMinimal"/> is true (email-gate skip), the query is replaced with only
    /// <c>source</c>, <c>email=unknown</c>, and optional <c>campaign</c> — no <c>leadId</c> or other inherited params.
    /// </summary>
    private static string MergeAttributionIntoRedirect(
        string redirectUrl,
        EventSource source,
        string? campaign,
        bool appendEmailCaptured,
        string? capturedEmail = null,
        string? attributionSourceRaw = null,
        bool unknownEmailSkipMinimal = false)
    {
        if (string.IsNullOrWhiteSpace(redirectUrl) || !Uri.TryCreate(redirectUrl, UriKind.Absolute, out var uri))
        {
            return redirectUrl;
        }

        try
        {
            var ub = new UriBuilder(uri);

            if (unknownEmailSkipMinimal)
            {
                var tokenSkip = VisitorAttributionService.ToAttributionToken(source);
                var sourceForSkip = !string.IsNullOrWhiteSpace(attributionSourceRaw)
                    ? attributionSourceRaw.Trim().ToLowerInvariant()
                    : tokenSkip;
                var minimal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = sourceForSkip,
                    ["email"] = "unknown"
                };
                if (!string.IsNullOrWhiteSpace(campaign))
                {
                    minimal["campaign"] = campaign.Trim();
                }

                var qsSkip = QueryString.Create(minimal.Select(static kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)));
                ub.Query = qsSkip.HasValue ? qsSkip.Value.TrimStart('?') : string.Empty;
                return ub.Uri.AbsoluteUri;
            }

            var rawQuery = ub.Query ?? string.Empty;
            if (rawQuery.StartsWith("?", StringComparison.Ordinal))
            {
                rawQuery = rawQuery[1..];
            }

            var parsed = QueryHelpers.ParseQuery(rawQuery);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in parsed)
            {
                if (kv.Value.Count > 0)
                {
                    dict[kv.Key] = kv.Value[^1] ?? string.Empty;
                }
            }

            var token = VisitorAttributionService.ToAttributionToken(source);
            var sourceForLanding = !string.IsNullOrWhiteSpace(attributionSourceRaw)
                ? attributionSourceRaw.Trim().ToLowerInvariant()
                : token;

            dict["ls_src"] = token;
            dict["src"] = token;
            dict["source"] = sourceForLanding;

            if (!string.IsNullOrWhiteSpace(capturedEmail))
            {
                dict["email"] = capturedEmail.Trim();
            }
            else if (appendEmailCaptured)
            {
                dict["emailCaptured"] = "true";
            }

            if (!string.IsNullOrWhiteSpace(campaign))
            {
                var cmpTrim = campaign.Trim();
                dict["ls_cmp"] = cmpTrim;
                dict["campaign"] = cmpTrim;
                if (!dict.ContainsKey("utm_campaign"))
                {
                    dict["utm_campaign"] = cmpTrim;
                }
            }

            if (!dict.ContainsKey("utm_source"))
            {
                dict["utm_source"] = token;
            }

            var qs = QueryString.Create(dict.Select(static kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)));
            ub.Query = qs.HasValue ? qs.Value.TrimStart('?') : string.Empty;
            return ub.Uri.AbsoluteUri;
        }
        catch
        {
            return redirectUrl;
        }
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

    /// <summary>Primary path; some browser extensions block URLs containing <c>/track/</c>. Use <c>/api/ingest/site-activity</c> from the web snippet.</summary>
    [HttpPost("event")]
    [HttpPost("~/api/ingest/site-activity")]
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

    private Uri ResolveSafeRedirectAbsolute(string? redirect)
    {
        var configured = configuration["Tracking:DefaultRedirectUrl"]?.Trim();
        var defaultUri =
            !string.IsNullOrWhiteSpace(configured) &&
            RedirectSafety.TryGetAllowedAbsoluteRedirect(configured, out var parsed) &&
            parsed is not null
                ? parsed
                : RedirectSafety.DefaultHiperbrainsSite;

        return RedirectSafety.SafeRedirectDestination(redirect, defaultUri);
    }

    private void AppendVisitorCookie(string visitorId)
    {
        Response.Cookies.Append("visitorId", visitorId, new CookieOptions
        {
            HttpOnly = false,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
    }

    private string GetOrCreateVisitorId()
    {
        if (Request.Cookies.TryGetValue("visitorId", out var visitorId) && !string.IsNullOrWhiteSpace(visitorId))
        {
            return visitorId;
        }

        return Guid.NewGuid().ToString("N");
    }

    /// <summary>Prefer explicit id (e.g. from site script), then first-party tracking cookie set by <c>/r</c>.</summary>
    private string? TryResolveVisitorId(string? explicitVisitorId)
    {
        if (!string.IsNullOrWhiteSpace(explicitVisitorId))
        {
            return explicitVisitorId.Trim();
        }

        if (Request.Cookies.TryGetValue("visitorId", out var fromCookie) && !string.IsNullOrWhiteSpace(fromCookie))
        {
            return fromCookie.Trim();
        }

        return null;
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
