using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategoryRule;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryReceiptCategoryMapping;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryCategoryRule;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryReceiptCategoryMapping;
using Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategoryRule;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategories;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategoryRules;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryReceiptCategoryMappings;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/grocery-categories")]
public class GroceryCategoriesController(
    GetGroceryCategoriesQueryHandler getCategories,
    GetGroceryCategoryRulesQueryHandler getCategoryRules,
    CreateGroceryCategoryCommandHandler createCategory,
    UpdateGroceryCategoryCommandHandler updateCategory,
    DeleteGroceryCategoryCommandHandler deleteCategory,
    CreateGroceryCategoryRuleCommandHandler createCategoryRule,
    DeleteGroceryCategoryRuleCommandHandler deleteCategoryRule,
    UpdateGroceryCategoryRuleCommandHandler updateCategoryRule,
    GetGroceryReceiptCategoryMappingsQueryHandler getReceiptMappings,
    CreateGroceryReceiptCategoryMappingCommandHandler createReceiptMapping,
    DeleteGroceryReceiptCategoryMappingCommandHandler deleteReceiptMapping) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await getCategories.HandleAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGroceryCategoryRequest body, CancellationToken ct)
    {
        return Ok(await createCategory.HandleAsync(
            new CreateGroceryCategoryCommand(body.Name, body.Color, body.Pattern, body.Value), ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateGroceryCategoryRequest body, CancellationToken ct)
    {
        var result = await updateCategory.HandleAsync(new UpdateGroceryCategoryCommand(id, body.Name, body.Color), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var (found, isProtected) = await deleteCategory.HandleAsync(new DeleteGroceryCategoryCommand(id), ct);
        if (!found) return NotFound();
        if (isProtected) return Conflict("This is a system category and cannot be deleted.");
        return NoContent();
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules(CancellationToken ct) =>
        Ok(await getCategoryRules.HandleAsync(ct));

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] CreateGroceryCategoryRuleRequest body, CancellationToken ct)
    {
        var result = await createCategoryRule.HandleAsync(
            new CreateGroceryCategoryRuleCommand(body.CategoryId, body.Pattern, body.Value), ct);
        return result is null ? NotFound("Grocery category not found.") : Ok(result);
    }

    [HttpPut("rules/{id:int}")]
    public async Task<IActionResult> UpdateRule(int id, [FromBody] UpdateGroceryCategoryRuleRequest body, CancellationToken ct)
    {
        var updated = await updateCategoryRule.HandleAsync(new UpdateGroceryCategoryRuleCommand(id, body.Pattern, body.Value), ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("rules/{id:int}")]
    public async Task<IActionResult> DeleteRule(int id, CancellationToken ct)
    {
        var deleted = await deleteCategoryRule.HandleAsync(new DeleteGroceryCategoryRuleCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("receipt-mappings")]
    public async Task<IActionResult> GetReceiptMappings(CancellationToken ct) =>
        Ok(await getReceiptMappings.HandleAsync(ct));

    [HttpPost("receipt-mappings")]
    public async Task<IActionResult> CreateReceiptMapping([FromBody] CreateReceiptMappingRequest body, CancellationToken ct)
    {
        var (result, isConflict) = await createReceiptMapping.HandleAsync(
            new CreateGroceryReceiptCategoryMappingCommand(body.ReceiptCategoryName, body.GroceryCategoryId), ct);
        if (isConflict) return Conflict($"A mapping for '{body.ReceiptCategoryName}' already exists.");
        return result is null ? NotFound() : Created($"/api/grocery-categories/receipt-mappings/{result.Id}", result);
    }

    [HttpDelete("receipt-mappings/{id:int}")]
    public async Task<IActionResult> DeleteReceiptMapping(int id, CancellationToken ct)
    {
        var deleted = await deleteReceiptMapping.HandleAsync(new DeleteGroceryReceiptCategoryMappingCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }
}

public record CreateGroceryCategoryRequest(string Name, string? Color, string? Pattern, decimal? Value = null);
public record UpdateGroceryCategoryRequest(string? Name, string? Color);
public record CreateGroceryCategoryRuleRequest(int CategoryId, string? Pattern, decimal? Value);
public record UpdateGroceryCategoryRuleRequest(string? Pattern, decimal? Value);
public record CreateReceiptMappingRequest(string ReceiptCategoryName, int GroceryCategoryId);
