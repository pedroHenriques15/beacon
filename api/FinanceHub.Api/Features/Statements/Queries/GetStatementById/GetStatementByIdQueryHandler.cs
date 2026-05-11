using FinanceHub.Api.Data;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Statements.Queries.GetStatementById;

public class GetStatementByIdQueryHandler(AppDbContext db, ILogger<GetStatementByIdQueryHandler> logger)
{
    public async Task<MonthlyStatement?> HandleAsync(GetStatementByIdQuery query, CancellationToken ct = default)
    {
        logger.LogInformation("GetStatementById: id={Id}", query.Id);
        return await db.MonthlyStatements
            .Include(s => s.Transactions.OrderBy(tx => tx.DatePosting))
                .ThenInclude(t => t.Category)
            .FirstOrDefaultAsync(s => s.Id == query.Id, ct);
    }
}
