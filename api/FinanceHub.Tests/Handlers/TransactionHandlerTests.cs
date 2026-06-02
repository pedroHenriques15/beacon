using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Transactions.Commands.BulkDeleteTransactions;
using FinanceHub.Api.Features.Transactions.Commands.DeleteTransaction;
using FinanceHub.Api.Features.Transactions.Commands.MarkTransfers;
using FinanceHub.Api.Features.Transactions.Commands.SetTransactionCategory;
using FinanceHub.Api.Features.Transactions.Queries.GetTransactions;
using FinanceHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceHub.Tests.Handlers;

public class TransactionHandlerTests
{
    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(MonthlyStatement stmt, Transaction tx1, Transaction tx2)> SeedTwoTransactionsAsync(
        AppDbContext db, string bank = "ACTIVOBANK")
    {
        var stmt = new MonthlyStatement
        {
            Bank = bank,
            Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1),
            PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "TRANSFER OUT", Amount = 500, Type = "debit",
                    DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 1500 },
                new Transaction { Description = "TRANSFER IN",  Amount = 500, Type = "credit",
                    DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 2000 }
            ]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var txs = stmt.Transactions.ToList();
        return (stmt, txs[0], txs[1]);
    }

    [Fact]
    public async Task DeleteTransaction_RemovesTransactionFromDb()
    {
        await using var db = CreateDb(nameof(DeleteTransaction_RemovesTransactionFromDb));
        var (_, tx1, _) = await SeedTwoTransactionsAsync(db);

        var handler = new DeleteTransactionCommandHandler(db, NullLogger<DeleteTransactionCommandHandler>.Instance);
        var result = await handler.HandleAsync(new DeleteTransactionCommand(tx1.Id));

        Assert.True(result);
        Assert.Null(await db.Transactions.FindAsync(tx1.Id));
    }

    [Fact]
    public async Task DeleteTransaction_ReturnsFalseForMissingTransaction()
    {
        await using var db = CreateDb(nameof(DeleteTransaction_ReturnsFalseForMissingTransaction));

        var handler = new DeleteTransactionCommandHandler(db, NullLogger<DeleteTransactionCommandHandler>.Instance);
        var result = await handler.HandleAsync(new DeleteTransactionCommand(9999));

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteTransaction_DoesNotAffectOtherTransactions()
    {
        await using var db = CreateDb(nameof(DeleteTransaction_DoesNotAffectOtherTransactions));
        var (_, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new DeleteTransactionCommandHandler(db, NullLogger<DeleteTransactionCommandHandler>.Instance);
        await handler.HandleAsync(new DeleteTransactionCommand(tx1.Id));

        var remaining = await db.Transactions.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(tx2.Id, remaining[0].Id);
    }

    [Fact]
    public async Task DeleteTransaction_TransferPairRemainsMarked()
    {
        await using var db = CreateDb(nameof(DeleteTransaction_TransferPairRemainsMarked));
        var (_, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var markHandler = new MarkTransfersCommandHandler(db, NullLogger<MarkTransfersCommandHandler>.Instance);
        await markHandler.HandleAsync(new MarkTransfersCommand([tx1.Id, tx2.Id]));

        var deleteHandler = new DeleteTransactionCommandHandler(db, NullLogger<DeleteTransactionCommandHandler>.Instance);
        await deleteHandler.HandleAsync(new DeleteTransactionCommand(tx1.Id));

        var pair = await db.Transactions.FindAsync(tx2.Id);
        Assert.NotNull(pair);
        Assert.True(pair.IsExcluded);
    }

    [Fact]
    public async Task DeleteTransaction_DeletesStatementWhenLastTransactionRemoved()
    {
        await using var db = CreateDb(nameof(DeleteTransaction_DeletesStatementWhenLastTransactionRemoved));
        var (stmt, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new DeleteTransactionCommandHandler(db, NullLogger<DeleteTransactionCommandHandler>.Instance);
        await handler.HandleAsync(new DeleteTransactionCommand(tx1.Id));
        await handler.HandleAsync(new DeleteTransactionCommand(tx2.Id));

        Assert.Null(await db.MonthlyStatements.FindAsync(stmt.Id));
    }

    [Fact]
    public async Task DeleteTransaction_DoesNotDeleteStatementWhenTransactionsRemain()
    {
        await using var db = CreateDb(nameof(DeleteTransaction_DoesNotDeleteStatementWhenTransactionsRemain));
        var (stmt, tx1, _) = await SeedTwoTransactionsAsync(db);

        var handler = new DeleteTransactionCommandHandler(db, NullLogger<DeleteTransactionCommandHandler>.Instance);
        await handler.HandleAsync(new DeleteTransactionCommand(tx1.Id));

        Assert.NotNull(await db.MonthlyStatements.FindAsync(stmt.Id));
    }

    [Fact]
    public async Task MarkTransfers_SetsIsExcludedTrue()
    {
        await using var db = CreateDb(nameof(MarkTransfers_SetsIsExcludedTrue));
        var (_, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new MarkTransfersCommandHandler(db, NullLogger<MarkTransfersCommandHandler>.Instance);
        await handler.HandleAsync(new MarkTransfersCommand([tx1.Id, tx2.Id]));

        var updated = await db.Transactions.ToListAsync();
        Assert.All(updated, t => Assert.True(t.IsExcluded));
    }

    [Fact]
    public async Task MarkTransfers_WithUnmarkTrue_SetsIsExcludedFalse()
    {
        await using var db = CreateDb(nameof(MarkTransfers_WithUnmarkTrue_SetsIsExcludedFalse));
        var (_, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new MarkTransfersCommandHandler(db, NullLogger<MarkTransfersCommandHandler>.Instance);
        await handler.HandleAsync(new MarkTransfersCommand([tx1.Id, tx2.Id]));
        await handler.HandleAsync(new MarkTransfersCommand([tx1.Id, tx2.Id], Unmark: true));

        var updated = await db.Transactions.ToListAsync();
        Assert.All(updated, t => Assert.False(t.IsExcluded));
    }

    [Fact]
    public async Task MarkTransfers_OnlyAffectsSpecifiedIds()
    {
        await using var db = CreateDb(nameof(MarkTransfers_OnlyAffectsSpecifiedIds));
        var (_, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new MarkTransfersCommandHandler(db, NullLogger<MarkTransfersCommandHandler>.Instance);
        await handler.HandleAsync(new MarkTransfersCommand([tx1.Id]));

        var reloaded1 = await db.Transactions.FindAsync(tx1.Id);
        var reloaded2 = await db.Transactions.FindAsync(tx2.Id);
        Assert.True(reloaded1!.IsExcluded);
        Assert.False(reloaded2!.IsExcluded);
    }

    [Fact]
    public async Task MarkTransfers_EmptyIds_DoesNotThrow()
    {
        await using var db = CreateDb(nameof(MarkTransfers_EmptyIds_DoesNotThrow));
        await SeedTwoTransactionsAsync(db);

        var handler = new MarkTransfersCommandHandler(db, NullLogger<MarkTransfersCommandHandler>.Instance);
        var ex = await Record.ExceptionAsync(() => handler.HandleAsync(new MarkTransfersCommand([])));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SetTransactionCategory_AssignsCategoryAndSetsManualFlag()
    {
        await using var db = CreateDb(nameof(SetTransactionCategory_AssignsCategoryAndSetsManualFlag));
        var cat = new Category { Name = "Food", Color = "#ff0000" };
        db.Categories.Add(cat);
        var stmt = new MonthlyStatement
        {
            Bank = "BPI", Account = "A", PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions = [new Transaction { Description = "LIDL", Amount = 25, Type = "debit",
                DatePosting = new DateOnly(2026, 1, 10), DateValue = new DateOnly(2026, 1, 10), Balance = 975 }]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var tx = stmt.Transactions.First();
        var handler = new SetTransactionCategoryCommandHandler(db, NullLogger<SetTransactionCategoryCommandHandler>.Instance);
        var result = await handler.HandleAsync(new SetTransactionCategoryCommand(tx.Id, cat.Id, null));

        Assert.NotNull(result);
        Assert.Equal(cat.Id, result.CategoryId);
        Assert.True(result.CategorySetManually);
        Assert.Null(result.CategoryRuleId);
    }

    [Fact]
    public async Task SetTransactionCategory_ClearsExistingRuleLink()
    {
        await using var db = CreateDb(nameof(SetTransactionCategory_ClearsExistingRuleLink));
        var cat1 = new Category { Name = "Food", Color = "#ff0000" };
        var cat2 = new Category { Name = "Shopping", Color = "#00ff00" };
        db.Categories.AddRange(cat1, cat2);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat1.Id, Pattern = "LIDL" };
        db.CategoryRules.Add(rule);
        var stmt = new MonthlyStatement
        {
            Bank = "BPI", Account = "A", PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions = [new Transaction { Description = "LIDL", Amount = 25, Type = "debit",
                DatePosting = new DateOnly(2026, 1, 10), DateValue = new DateOnly(2026, 1, 10), Balance = 975,
                CategoryId = cat1.Id, CategoryRuleId = rule.Id }]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var tx = stmt.Transactions.First();
        var handler = new SetTransactionCategoryCommandHandler(db, NullLogger<SetTransactionCategoryCommandHandler>.Instance);
        await handler.HandleAsync(new SetTransactionCategoryCommand(tx.Id, cat2.Id, null));

        var updated = await db.Transactions.FindAsync(tx.Id);
        Assert.Equal(cat2.Id, updated!.CategoryId);
        Assert.Null(updated.CategoryRuleId);
        Assert.True(updated.CategorySetManually);
    }

    [Fact]
    public async Task SetTransactionCategory_WithDeleteRuleId_RemovesRule()
    {
        await using var db = CreateDb(nameof(SetTransactionCategory_WithDeleteRuleId_RemovesRule));
        var cat = new Category { Name = "Food", Color = "#ff0000" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var rule = new CategoryRule { CategoryId = cat.Id, Pattern = "LIDL" };
        db.CategoryRules.Add(rule);
        var stmt = new MonthlyStatement
        {
            Bank = "BPI", Account = "A", PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions = [new Transaction { Description = "LIDL", Amount = 25, Type = "debit",
                DatePosting = new DateOnly(2026, 1, 10), DateValue = new DateOnly(2026, 1, 10), Balance = 975 }]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var tx = stmt.Transactions.First();
        var handler = new SetTransactionCategoryCommandHandler(db, NullLogger<SetTransactionCategoryCommandHandler>.Instance);
        await handler.HandleAsync(new SetTransactionCategoryCommand(tx.Id, cat.Id, rule.Id));

        Assert.False(await db.CategoryRules.AnyAsync(r => r.Id == rule.Id));
    }

    [Fact]
    public async Task SetTransactionCategory_ReturnsNullForMissingTransaction()
    {
        await using var db = CreateDb(nameof(SetTransactionCategory_ReturnsNullForMissingTransaction));
        var handler = new SetTransactionCategoryCommandHandler(db, NullLogger<SetTransactionCategoryCommandHandler>.Instance);
        var result = await handler.HandleAsync(new SetTransactionCategoryCommand(9999, null, null));
        Assert.Null(result);
    }

    [Fact]
    public async Task SetTransactionCategory_WithNullCategoryId_Uncategorizes()
    {
        await using var db = CreateDb(nameof(SetTransactionCategory_WithNullCategoryId_Uncategorizes));
        var cat = new Category { Name = "Food", Color = "#ff0000" };
        db.Categories.Add(cat);
        var stmt = new MonthlyStatement
        {
            Bank = "BPI", Account = "A", PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions = [new Transaction { Description = "LIDL", Amount = 25, Type = "debit",
                DatePosting = new DateOnly(2026, 1, 10), DateValue = new DateOnly(2026, 1, 10), Balance = 975,
                CategoryId = cat.Id }]
        };
        db.MonthlyStatements.Add(stmt);
        await db.SaveChangesAsync();

        var tx = stmt.Transactions.First();
        var handler = new SetTransactionCategoryCommandHandler(db, NullLogger<SetTransactionCategoryCommandHandler>.Instance);
        var result = await handler.HandleAsync(new SetTransactionCategoryCommand(tx.Id, null, null));

        Assert.NotNull(result);
        Assert.Null(result.CategoryId);
    }

    private static async Task SeedTransactionsForQueryAsync(AppDbContext db)
    {
        var cat = new Category { Name = "Food", Color = "#ff0000" };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var stmtA = new MonthlyStatement
        {
            Bank = "ACTIVOBANK", Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Transactions =
            [
                new Transaction { Description = "LIDL Lisboa",   Amount = 30,  Type = "debit",
                    DatePosting = new DateOnly(2026, 1, 5), DateValue = new DateOnly(2026, 1, 5), Balance = 970,
                    CategoryId = cat.Id },
                new Transaction { Description = "SALARY",        Amount = 1500, Type = "credit",
                    DatePosting = new DateOnly(2026, 1, 10), DateValue = new DateOnly(2026, 1, 10), Balance = 2470 }
            ]
        };
        var stmtB = new MonthlyStatement
        {
            Bank = "BPI", Account = "PT51",
            PeriodFrom = new DateOnly(2026, 2, 1), PeriodTo = new DateOnly(2026, 2, 28),
            Transactions =
            [
                new Transaction { Description = "CONTINENTE",    Amount = 50, Type = "debit",
                    DatePosting = new DateOnly(2026, 2, 3), DateValue = new DateOnly(2026, 2, 3), Balance = 1000 }
            ]
        };
        db.MonthlyStatements.AddRange(stmtA, stmtB);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTransactions_NoFilters_ReturnsAll()
    {
        await using var db = CreateDb(nameof(GetTransactions_NoFilters_ReturnsAll));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, null, null, null, null, Take: 100));

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task GetTransactions_FilterByBank_ReturnsOnlyMatchingBank()
    {
        await using var db = CreateDb(nameof(GetTransactions_FilterByBank_ReturnsOnlyMatchingBank));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery("activobank", null, null, null, null, Take: 100));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal("ACTIVOBANK", item.Bank));
    }

    [Fact]
    public async Task GetTransactions_FilterByMonth_ReturnsOnlyThatMonth()
    {
        await using var db = CreateDb(nameof(GetTransactions_FilterByMonth_ReturnsOnlyThatMonth));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, "2026-01", null, null, null, Take: 100));

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetTransactions_FilterByType_ReturnsOnlyMatchingType()
    {
        await using var db = CreateDb(nameof(GetTransactions_FilterByType_ReturnsOnlyMatchingType));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, null, "credit", null, null, Take: 100));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("SALARY", result.Items[0].Description);
    }

    [Fact]
    public async Task GetTransactions_FilterCategoryUnknown_ReturnsUncategorizedOnly()
    {
        await using var db = CreateDb(nameof(GetTransactions_FilterCategoryUnknown_ReturnsUncategorizedOnly));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, null, null, "unknown", null, Take: 100));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.CategoryId));
    }

    [Fact]
    public async Task GetTransactions_FilterBySearch_ReturnsDescriptionMatches()
    {
        await using var db = CreateDb(nameof(GetTransactions_FilterBySearch_ReturnsDescriptionMatches));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, null, null, null, "LIDL", Take: 100));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("LIDL Lisboa", result.Items[0].Description);
    }

    [Fact]
    public async Task GetTransactions_Pagination_RespectsSkipAndTake()
    {
        await using var db = CreateDb(nameof(GetTransactions_Pagination_RespectsSkipAndTake));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var page1 = await handler.HandleAsync(new GetTransactionsQuery(null, null, null, null, null, Skip: 0, Take: 2));
        var page2 = await handler.HandleAsync(new GetTransactionsQuery(null, null, null, null, null, Skip: 2, Take: 2));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(3, page2.TotalCount);
        Assert.Single(page2.Items);
    }

    [Fact]
    public async Task GetTransactions_FilterByType_TotalsStillReflectBothTypes()
    {
        await using var db = CreateDb(nameof(GetTransactions_FilterByType_TotalsStillReflectBothTypes));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, null, "credit", null, null, Take: 100));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("SALARY", result.Items[0].Description);
        Assert.Equal(1500m, result.TotalCredit);
        Assert.Equal(80m, result.TotalDebit);
    }

    [Fact]
    public async Task BulkDeleteTransactions_DeletesAllSpecifiedTransactions()
    {
        await using var db = CreateDb(nameof(BulkDeleteTransactions_DeletesAllSpecifiedTransactions));
        var (_, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new BulkDeleteTransactionsCommandHandler(db, NullLogger<BulkDeleteTransactionsCommandHandler>.Instance);
        await handler.HandleAsync(new BulkDeleteTransactionsCommand([tx1.Id, tx2.Id]));

        Assert.Empty(await db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task BulkDeleteTransactions_DeletesEmptyStatementsAfterBulkDelete()
    {
        await using var db = CreateDb(nameof(BulkDeleteTransactions_DeletesEmptyStatementsAfterBulkDelete));
        var (stmt, tx1, tx2) = await SeedTwoTransactionsAsync(db);

        var handler = new BulkDeleteTransactionsCommandHandler(db, NullLogger<BulkDeleteTransactionsCommandHandler>.Instance);
        await handler.HandleAsync(new BulkDeleteTransactionsCommand([tx1.Id, tx2.Id]));

        Assert.Null(await db.MonthlyStatements.FindAsync(stmt.Id));
    }

    [Fact]
    public async Task BulkDeleteTransactions_KeepsStatementWithRemainingTransactions()
    {
        await using var db = CreateDb(nameof(BulkDeleteTransactions_KeepsStatementWithRemainingTransactions));
        var (stmt, tx1, _) = await SeedTwoTransactionsAsync(db);

        var handler = new BulkDeleteTransactionsCommandHandler(db, NullLogger<BulkDeleteTransactionsCommandHandler>.Instance);
        await handler.HandleAsync(new BulkDeleteTransactionsCommand([tx1.Id]));

        Assert.NotNull(await db.MonthlyStatements.FindAsync(stmt.Id));
    }

    [Fact]
    public async Task GetTransactions_TakeClampedTo500()
    {
        await using var db = CreateDb(nameof(GetTransactions_TakeClampedTo500));
        await SeedTransactionsForQueryAsync(db);

        var handler = new GetTransactionsQueryHandler(db, NullLogger<GetTransactionsQueryHandler>.Instance);
        var result = await handler.HandleAsync(new GetTransactionsQuery(null, null, null, null, null, Take: 9999));

        Assert.Equal(3, result.TotalCount);
    }
}
