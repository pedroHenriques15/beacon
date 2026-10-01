using Beacon.Api.Features.Categories.Commands.CreateCategory;
using Beacon.Api.Features.Categories.Commands.CreateCategoryRule;
using Beacon.Api.Features.Categories.Commands.DeleteCategory;
using Beacon.Api.Features.Categories.Commands.DeleteCategoryRule;
using Beacon.Api.Features.Categories.Commands.UpdateCategory;
using Beacon.Api.Features.Categories.Commands.UpdateCategoryRule;
using Beacon.Api.Features.Categories.Queries.GetCategories;
using Beacon.Api.Features.Categories.Queries.GetCategoryRules;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(
    GetCategoriesQueryHandler getCategories,
    GetCategoryRulesQueryHandler getCategoryRules,
    CreateCategoryCommandHandler createCategory,
    UpdateCategoryCommandHandler updateCategory,
    DeleteCategoryCommandHandler deleteCategory,
    CreateCategoryRuleCommandHandler createCategoryRule,
    DeleteCategoryRuleCommandHandler deleteCategoryRule,
    UpdateCategoryRuleCommandHandler updateCategoryRule) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await getCategories.HandleAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest body, CancellationToken ct)
    {
        return Ok(await createCategory.HandleAsync(new CreateCategoryCommand(body.Name, body.Color, body.Pattern, body.Value), ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest body, CancellationToken ct)
    {
        var result = await updateCategory.HandleAsync(new UpdateCategoryCommand(id, body.Name, body.Color), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var (found, isProtected) = await deleteCategory.HandleAsync(new DeleteCategoryCommand(id), ct);
        if (!found) return NotFound();
        if (isProtected) return Conflict("This is a system category and cannot be deleted.");
        return NoContent();
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules(CancellationToken ct) =>
        Ok(await getCategoryRules.HandleAsync(ct));

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] CreateCategoryRuleRequest body, CancellationToken ct)
    {
        var result = await createCategoryRule.HandleAsync(new CreateCategoryRuleCommand(body.CategoryId, body.Pattern, body.Value), ct);
        return result is null ? NotFound("Category not found.") : Ok(result);
    }

    [HttpPut("rules/{id:int}")]
    public async Task<IActionResult> UpdateRule(int id, [FromBody] UpdateCategoryRuleRequest body, CancellationToken ct)
    {
        var updated = await updateCategoryRule.HandleAsync(new UpdateCategoryRuleCommand(id, body.Pattern, body.Value), ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("rules/{id:int}")]
    public async Task<IActionResult> DeleteRule(int id, CancellationToken ct)
    {
        var deleted = await deleteCategoryRule.HandleAsync(new DeleteCategoryRuleCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }
}

public record CreateCategoryRequest(string Name, string? Color, string? Pattern, decimal? Value = null);
public record UpdateCategoryRequest(string? Name, string? Color);
public record CreateCategoryRuleRequest(int CategoryId, string? Pattern, decimal? Value);
public record UpdateCategoryRuleRequest(string? Pattern, decimal? Value);
