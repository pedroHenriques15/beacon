namespace Beacon.Api.Features.Groceries.Commands.MarkGroceryItemsExcluded;

public record MarkGroceryItemsExcludedCommand(int[] ItemIds, bool Unmark = false);
