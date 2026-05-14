using LeadScoring.Api.Models;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/batch")]
public class BatchController(IBatchProcessingService batchProcessingService, ILogger<BatchController> logger) : ControllerBase
{
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int take = 200, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await batchProcessingService.GetBatchLogHistoryAsync(take, cancellationToken);
        return Ok(rows);
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] CampaignBatchType batchType, CancellationToken cancellationToken)
    {
        var result = await batchProcessingService.PreviewAsync(batchType, cancellationToken);
        return Ok(result);
    }

    [HttpPost("run-manual")]
    public async Task<IActionResult> RunManual([FromQuery] CampaignBatchType batchType, [FromBody] BatchManualRunRequestDto? request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await batchProcessingService.RunManualAsync(batchType, request?.Scope, request?.MaxLeads, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual batch run failed for {BatchType}", batchType);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Manual batch run failed due to internal error." });
        }
    }

    [HttpPost("run-manual/start")]
    public async Task<IActionResult> StartManual([FromQuery] CampaignBatchType batchType, [FromBody] BatchManualRunRequestDto? request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await batchProcessingService.StartManualAsync(batchType, request?.Scope, request?.MaxLeads, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual batch start failed for {BatchType}", batchType);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Manual batch start failed due to internal error." });
        }
    }

    [HttpGet("run-manual/status/{jobId:guid}")]
    public IActionResult ManualStatus(Guid jobId)
    {
        var status = batchProcessingService.GetManualStatus(jobId);
        if (status is null)
        {
            return NotFound(new { message = "Manual batch job not found." });
        }

        return Ok(status);
    }

    /// <summary>QA: send manual-batch-style HTML only to listed addresses — no mirror emails, observers, logs, or lead updates.</summary>
    [HttpPost("test-marketing-send")]
    [HttpPost("test-email")]
    public async Task<IActionResult> TestMarketingSend([FromBody] TestMarketingEmailRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await batchProcessingService.SendTestMarketingEmailsAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test marketing send failed for {BatchType}", request.BatchType);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Test send failed due to internal error." });
        }
    }

    [HttpPost("retry/{batchId:long}")]
    public async Task<IActionResult> Retry(long batchId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await batchProcessingService.RetryFailedLeadsAsync(batchId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Retry failed for batch {BatchId}", batchId);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected retry failure for batch {BatchId}", batchId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Retry failed due to internal error." });
        }
    }
}
