using System.Text.Json;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/company-product-configs")]
public class CompanyProductConfigsController(LeadScoringDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertCompanyProductConfigRequest request)
    {
        if (!TryNormalizeRequest(request, out var normalizedItems, out var errorMessage))
        {
            return BadRequest(errorMessage);
        }

        var configJson = JsonSerializer.Serialize(normalizedItems);
        var nextProductId = await GetNextProductIdAsync();
        var entity = new CompanyProductConfig
        {
            Id = Guid.NewGuid(),
            CompanyName = request.CompanyName.Trim(),
            ProductName = request.ProductName.Trim(),
            ProductId = nextProductId,
            ProductEventConfigJson = configJson,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.CompanyProductConfigs.Add(entity);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("A config already exists for this company, product, and product ID.");
        }

        var dto = CompanyProductConfigMapper.ToDto(
            entity.Id,
            entity.CompanyName,
            entity.ProductName,
            entity.ProductId,
            entity.ProductEventConfigJson,
            entity.CreatedAtUtc);

        return Ok(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertCompanyProductConfigRequest request)
    {
        if (!TryNormalizeRequest(request, out var normalizedItems, out var errorMessage))
        {
            return BadRequest(errorMessage);
        }

        var entity = await db.CompanyProductConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
        {
            return NotFound("Company product config not found.");
        }

        entity.CompanyName = request.CompanyName.Trim();
        entity.ProductName = request.ProductName.Trim();
        entity.ProductEventConfigJson = JsonSerializer.Serialize(normalizedItems);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("A config already exists for this company, product, and product ID.");
        }

        var dto = CompanyProductConfigMapper.ToDto(
            entity.Id,
            entity.CompanyName,
            entity.ProductName,
            entity.ProductId,
            entity.ProductEventConfigJson,
            entity.CreatedAtUtc);

        return Ok(dto);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? companyName = null)
    {
        var query = db.CompanyProductConfigs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(companyName))
        {
            var filter = companyName.Trim().ToLower();
            query = query.Where(x => x.CompanyName.ToLower().Contains(filter));
        }

        var records = await query
            .OrderBy(x => x.CompanyName)
            .ThenBy(x => x.ProductName)
            .ThenBy(x => x.ProductId)
            .Select(x => CompanyProductConfigMapper.ToDto(
                x.Id,
                x.CompanyName,
                x.ProductName,
                x.ProductId,
                x.ProductEventConfigJson,
                x.CreatedAtUtc))
            .ToListAsync();

        return Ok(records);
    }

    private static bool TryNormalizeRequest(
        UpsertCompanyProductConfigRequest request,
        out Dictionary<string, int> normalizedItems,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            normalizedItems = new();
            errorMessage = "Company name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            normalizedItems = new();
            errorMessage = "Product name is required.";
            return false;
        }

        if (request.ProductEventConfig.Count == 0)
        {
            normalizedItems = new();
            errorMessage = "At least one event config item is required.";
            return false;
        }

        normalizedItems = request.ProductEventConfig
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(
                x => x.Key.Trim(),
                x => Math.Max(0, x.Value),
                StringComparer.OrdinalIgnoreCase);

        if (normalizedItems.Count == 0)
        {
            errorMessage = "At least one valid event config item is required.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private async Task<int> GetNextProductIdAsync()
    {
        var max = await db.CompanyProductConfigs
            .Select(x => x.ProductId)
            .DefaultIfEmpty(0)
            .MaxAsync();
        return max + 1;
    }
}
