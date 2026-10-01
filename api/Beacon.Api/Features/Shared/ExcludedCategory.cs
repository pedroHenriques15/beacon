using Beacon.Api.Data;
using Beacon.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api.Features.Shared;

/// <summary>
/// An entity whose "Excluded" category drives whether it counts towards aggregates.
/// Implemented by <see cref="Transaction"/> and <see cref="GroceryItem"/>.
/// </summary>
public interface ICategorisedEntity
{
    int? CategoryId { get; set; }
    bool IsExcluded { get; set; }
}

/// <summary>
/// The "Excluded" category is authoritative for the IsExcluded flag: putting a row in it excludes
/// the row from spending/income aggregates, and taking it out puts the row back. Every code path
/// that assigns a category must go through <see cref="ApplyCategory"/> so the label and the flag
/// can never drift apart.
///
/// Transactions and grocery items each have their own category table, hence the two id lookups;
/// the category name is the same in both.
/// </summary>
public static class ExcludedCategory
{
    public const string Name = "Excluded";

    public static async Task<int?> GetIdAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.Categories.FirstOrDefaultAsync(c => c.Name == Name, ct))?.Id;

    public static async Task<int?> GetGroceryIdAsync(AppDbContext db, CancellationToken ct = default) =>
        (await db.GroceryCategories.FirstOrDefaultAsync(c => c.Name == Name, ct))?.Id;

    /// <summary>
    /// Assigns <paramref name="newCategoryId"/> to the entity and keeps its IsExcluded flag in sync.
    /// An entity excluded by a non-category source (e.g. a Trade Republic savings-plan buy, which is
    /// excluded with no category at all) keeps its flag when it is later given a category.
    /// </summary>
    public static void ApplyCategory<T>(T entity, int? newCategoryId, int? excludedCategoryId)
        where T : ICategorisedEntity
    {
        if (excludedCategoryId is not null)
        {
            if (newCategoryId == excludedCategoryId)
                entity.IsExcluded = true;
            else if (entity.CategoryId == excludedCategoryId)
                entity.IsExcluded = false;
        }

        entity.CategoryId = newCategoryId;
    }
}
