using FinanceHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceHub.Api.Features.Transactions.Queries.GetTransactions;

public class GetTransactionsQueryHandler(AppDbContext db, ILogger<GetTransactionsQueryHandler> logger)
{
    public async Task<PagedTransactionsResponse> HandleAsync(GetTransactionsQuery query, CancellationToken ct = default)
    {
        logger.LogInformation("GetTransactions: bank={Bank} month={Month} type={Type} skip={Skip} take={Take}",
            query.Bank, query.Month, query.Type, query.Skip, query.Take);

        var q = db.Transactions
            .Include(tx => tx.Statement)
            .Include(tx => tx.Category)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query.Bank))
            q = q.Where(tx => tx.Statement.Bank == query.Bank.ToUpper());

        if (!string.IsNullOrEmpty(query.Month) && DateOnly.TryParse(query.Month + "-01", out var md))
            q = q.Where(tx => tx.Statement.PeriodFrom.Year == md.Year && tx.Statement.PeriodFrom.Month == md.Month);

        if (query.CategoryFilter == "unknown")
            q = q.Where(tx => tx.CategoryId == null);
        else if (!string.IsNullOrEmpty(query.CategoryFilter) && int.TryParse(query.CategoryFilter, out var catId))
            q = q.Where(tx => tx.CategoryId == catId);

        if (!string.IsNullOrEmpty(query.Search))
            q = q.Where(tx => tx.Description.Contains(query.Search));

        var totalsQ = q.Where(tx => !tx.IsExcluded);

        if (!string.IsNullOrEmpty(query.Type))
            q = q.Where(tx => tx.Type == query.Type.ToLower());

        var take = Math.Clamp(query.Take, 1, 500);
        var totalCount = await q.CountAsync(ct);
        var totalCredit = await totalsQ.Where(tx => tx.Type == "credit").SumAsync(tx => tx.Amount, ct);
        var totalDebit = await totalsQ.Where(tx => tx.Type == "debit").SumAsync(tx => tx.Amount, ct);

        var ordered = (query.SortBy?.ToLower(), query.SortDir?.ToLower()) switch
        {
            ("bank", "asc") => q.OrderBy(tx => tx.Statement.Bank).ThenByDescending(tx => tx.Id),
            ("bank", _) => q.OrderByDescending(tx => tx.Statement.Bank).ThenByDescending(tx => tx.Id),
            ("description", "asc") => q.OrderBy(tx => tx.Description).ThenByDescending(tx => tx.Id),
            ("description", _) => q.OrderByDescending(tx => tx.Description).ThenByDescending(tx => tx.Id),
            ("category", "asc") => q.OrderBy(tx => tx.Category == null ? "zzz" : tx.Category.Name).ThenByDescending(tx => tx.Id),
            ("category", _) => q.OrderByDescending(tx => tx.Category == null ? "" : tx.Category.Name).ThenByDescending(tx => tx.Id),
            ("amount", "asc") => q.OrderBy(tx => tx.Amount).ThenByDescending(tx => tx.Id),
            ("amount", _) => q.OrderByDescending(tx => tx.Amount).ThenByDescending(tx => tx.Id),
            ("balance", "asc") => q.OrderBy(tx => tx.Balance).ThenByDescending(tx => tx.Id),
            ("balance", _) => q.OrderByDescending(tx => tx.Balance).ThenByDescending(tx => tx.Id),
            ("date", "asc") => q.OrderBy(tx => tx.DatePosting).ThenBy(tx => tx.Id),
            _ => q.OrderByDescending(tx => tx.DatePosting).ThenByDescending(tx => tx.Id),
        };

        var items = await ordered
            .Skip(query.Skip)
            .Take(take)
            .Select(tx => new GetTransactionsResponse(
                tx.Id, tx.StatementId, tx.Statement.Bank,
                tx.DatePosting, tx.DateValue, tx.Description,
                tx.Amount, tx.Type, tx.Balance,
                tx.CategoryId, tx.CategoryRuleId, tx.CategorySetManually, tx.IsExcluded,
                tx.Category == null ? null : new CategoryDto(tx.Category.Id, tx.Category.Name, tx.Category.Color)))
            .ToListAsync(ct);

        return new PagedTransactionsResponse(items, totalCount, totalCredit, totalDebit);
    }
}
