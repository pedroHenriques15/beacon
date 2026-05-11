using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Statements.Queries.GetStatements;

public class GetStatementsQueryHandler(AppDbContext db, ILogger<GetStatementsQueryHandler> logger)
{
    public async Task<List<GetStatementsResponse>> HandleAsync(GetStatementsQuery query, CancellationToken ct = default)
    {
        logger.LogInformation("GetStatements: bank={Bank}", query.Bank);
        var q = db.MonthlyStatements.AsQueryable();

        if (!string.IsNullOrEmpty(query.Bank))
            q = q.Where(s => s.Bank == query.Bank.ToUpper());

        return await q
            .OrderByDescending(s => s.PeriodFrom)
            .Select(s => new GetStatementsResponse(
                s.Id, s.Bank, s.Account, s.PeriodFrom, s.PeriodTo,
                s.Currency, s.OpeningBalance, s.ClosingBalance, s.SourceFile,
                s.PdfPath != null, s.Transactions.Count))
            .ToListAsync(ct);
    }
}
