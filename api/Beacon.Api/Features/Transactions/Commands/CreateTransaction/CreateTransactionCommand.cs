namespace Beacon.Api.Features.Transactions.Commands.CreateTransaction;

public record CreateTransactionCommand(
    int StatementId,
    DateOnly DatePosting,
    DateOnly DateValue,
    string Description,
    decimal Amount,
    string Type,
    decimal Balance,
    int? CategoryId);
