using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/leads")]
public class LeadsController(
    LeadScoringDbContext db,
    TokenService tokenService,
    IEmailService emailService,
    LeadImportService leadImportService) : ControllerBase
{
    [HttpPost("import-file")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportFile([FromForm] ImportLeadsRequest request)
    {
        var file = request.File;
        if (file is null)
        {
            return BadRequest("File is required.");
        }

        if (file.Length == 0)
        {
            return BadRequest("Empty file.");
        }

        var result = await leadImportService.ImportFromFileAsync(file, request.Source);
        return Ok(result);
    }

    [HttpPost("import-hubspot-csv")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportHubspotCsv([FromForm] ImportLeadsRequest request)
    {
        request.Source ??= "hubspot";
        return ImportFile(request);
    }

    [HttpPost("import-json")]
    public async Task<IActionResult> ImportJson([FromBody] LeadImportPayload payload)
    {
        if (payload.Leads.Count == 0)
        {
            return BadRequest("No leads provided.");
        }

        var result = await leadImportService.ImportFromPayloadAsync(payload);
        return Ok(result);
    }

    [HttpPost("{leadId:guid}/send-email")]
    public async Task<IActionResult> SendEmail(Guid leadId, [FromBody] SendEmailRequest request)
    {
        var lead = await db.Leads.FindAsync(leadId);
        if (lead is null)
        {
            return NotFound("Lead not found.");
        }

        var token = tokenService.CreateLeadToken(lead.Id);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var openPixel = $"{baseUrl}/track/open?token={Uri.EscapeDataString(token)}";
        var trackedLink = $"{baseUrl}/track/click?token={Uri.EscapeDataString(token)}&redirect={Uri.EscapeDataString(request.RedirectUrl)}";
        var html = $"{request.HtmlBody}<img alt=\"\" width=\"1\" height=\"1\" src=\"{openPixel}\" /><p><a href=\"{trackedLink}\">Continue</a></p>";

        await emailService.SendAsync(lead.Email, request.Subject, html);
        return Ok(new { message = "Email queued.", trackedLink, openPixel });
    }

    [HttpPost("{leadId:guid}/send-welcome-email")]
    public async Task<IActionResult> SendWelcomeEmail(Guid leadId)
    {
        var lead = await db.Leads.FindAsync(leadId);
        if (lead is null)
        {
            return NotFound("Lead not found.");
        }

        var template = await db.EmailTemplates
            .Where(t => t.IsActive && t.Stage == LeadStage.Cold)
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync();
        if (template is null)
        {
            return BadRequest("No active Cold stage template found.");
        }

        const string eventName = "welcome_email";
        var htmlBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);

        await emailService.SendAsync(lead.Email, template.Subject, htmlBody);

        return Ok(new
        {
            message = "Welcome email queued.",
            lead.Email
        });
    }

    private static string ResolveTemplate(string value, Lead lead, string eventName, bool trackingEnabled)
    {
        var leadId = lead.Id.ToString("D");
        var emailValue = trackingEnabled ? Uri.EscapeDataString(lead.Email) : lead.Email;
        var eventValue = trackingEnabled ? Uri.EscapeDataString(eventName) : eventName;
        var leadIdValue = trackingEnabled ? Uri.EscapeDataString(leadId) : leadId;

        return value
            .Replace("{{email}}", emailValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{event}}", eventValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{leadId}}", leadIdValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{stage}}", LeadStage.Cold.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
