using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/leads")]
public class LeadsController(
    LeadScoringDbContext db,
    LeadImportService leadImportService,
    VisitorAttributionService visitorAttributionService) : ControllerBase
{
    [HttpPost("email-exists")]
    public async Task<ActionResult<LeadEmailExistsResponse>> CheckEmailExists([FromBody] LeadEmailExistsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var lead = await db.Leads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Email, normalizedEmail));

        return Ok(new LeadEmailExistsResponse(
            Email: normalizedEmail,
            Exists: lead is not null,
            LeadId: lead?.Id));
    }

    [HttpPost("website-demo/submit")]
    public async Task<ActionResult<WebsiteDemoSubmitResponse>> SubmitWebsiteDemo([FromBody] WebsiteDemoSubmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VisitorId))
        {
            return BadRequest("visitorId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("email is required.");
        }

        var result = await visitorAttributionService.IdentifyVisitorAsync(request);

        return Ok(new WebsiteDemoSubmitResponse(
            result.Lead.Id,
            result.Lead.Email,
            result.LeadCreated,
            result.VisitorMapped,
            EventCreated: true));
    }

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
    public IActionResult SendEmail(Guid leadId, [FromBody] SendEmailRequest request)
    {
        return StatusCode(StatusCodes.Status410Gone, new
        {
            message = "Manual send-email endpoint is disabled. Use batch runner and follow-up scheduler flows only."
        });
    }

    [HttpPost("{leadId:guid}/send-welcome-email")]
    public IActionResult SendWelcomeEmail(Guid leadId)
    {
        return StatusCode(StatusCodes.Status410Gone, new
        {
            message = "Manual send-welcome-email endpoint is disabled. Use batch runner and follow-up scheduler flows only."
        });
    }

}
