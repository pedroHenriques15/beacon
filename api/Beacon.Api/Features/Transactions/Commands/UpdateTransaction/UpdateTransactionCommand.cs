namespace Beacon.Api.Features.Transactions.Commands.UpdateTransaction;

public record UpdateTransactionCommand(
    int Id,
    DateOnly? DatePosting,
    DateOnly? DateValue,
    string? Description,
    decimal? Amount,
    string? Type,
    decimal? Balance,
    int? CategoryId,
    bool? CategorySetManually,
    bool UnlinkTransfer,
    bool UnlinkCategory);
