using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Commands.CreateTransaction;

public class CreateTransactionCommandHandler(AppDbContext db, ILogger<CreateTransactionCommandHandler> logger)
{
    public async Task<(CreateTransactionResponse? result, string? error)> HandleAsync(
        CreateTransactionCommand cmd, CancellationToken ct = default)
    {
        logger.LogInformation("CreateTransaction: statementId={StatementId} description={Description} amount={Amount}", cmd.StatementId, cmd.Description, cmd.Amount);
        if (!await db.MonthlyStatements.AnyAsync(s => s.Id == cmd.StatementId, ct))
            return (null, "Statement not found.");

        if (string.IsNullOrWhiteSpace(cmd.Description))
            return (null, "Description is required.");

        if (cmd.Amount <= 0)
            return (null, "Amount must be positive.");

        var validTypes = new[] { "credit", "debit" };
        if (!validTypes.Contains(cmd.Type.ToLower()))
            return (null, "Type must be 'credit' or 'debit'.");

        var tx = new Transaction
        {
            StatementId            = cmd.StatementId,
            DatePosting            = cmd.DatePosting,
            DateValue              = cmd.DateValue,
            Description            = cmd.Description.Trim(),
            Amount                 = cmd.Amount,
            Type                   = cmd.Type.ToLower(),
            Balance                = cmd.Balance,
            CategoryId             = cmd.CategoryId,
            CategorySetManually    = cmd.CategoryId.HasValue,
        };

        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);

        return (new CreateTransactionResponse(
            tx.Id, tx.StatementId, tx.DatePosting, tx.DateValue,
            tx.Description, tx.Amount, tx.Type, tx.Balance,
            tx.CategoryId, tx.CategorySetManually, tx.IsExcluded), null);
    }
}
