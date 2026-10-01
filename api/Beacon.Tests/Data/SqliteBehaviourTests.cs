using Beacon.Api.Data;
using Beacon.Api.Features.Categories.Queries.GetCategories;
using Beacon.Api.Features.Groceries.Queries.GetGroceryItems;
using Beacon.Api.Features.Groceries.Queries.GetGroceryReceipts;
using Beacon.Api.Features.Investments.Commands.CreateInvestmentLot;
using Beacon.Api.Features.Transactions.Queries.GetTransactions;
using Beacon.Api.Models;
using Beacon.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests.Data;

/// <summary>
/// What the database engine itself must get right: decimal sums and sorts translated to SQL,
/// searches, sorts and unique names that treat case and accents as SQL Server did, decimals
/// held to their column's scale, and enforced foreign keys.
/// </summary>
public class SqliteBehaviourTests
{
    private static async Task<MonthlyStatement> SeedStatementAsync(
        AppDbContext db, params (string Description, decimal Amount, string Type, decimal Balance)[] rows)
    {
        var statement = new MonthlyStatement
        {
            Bank = "ACTIVOBANK",
            Account = "PT50",
            PeriodFrom = new DateOnly(2026, 1, 1),
            PeriodTo = new DateOnly(2026, 1, 31),
            SourceFile = "statement.pdf",
            Transactions = rows.Select((r, i) => new Transaction
            {
                Description = r.Description,
                Amount = r.Amount,
                Type = r.Type,
                Balance = r.Balance,
                DatePosting = new DateOnly(2026, 1, 1 + i),
                DateValue = new DateOnly(2026, 1, 1 + i),
            }).ToList(),
        };
        db.MonthlyStatements.Add(statement);
        await db.SaveChangesAsync();
        return statement;
    }

    private static GetTransactionsQueryHandler TransactionsHandler(AppDbContext db) =>
        new(db, NullLogger<GetTransactionsQueryHandler>.Instance);

    [Fact]
    public async Task GetTransactions_SortsByAmountAndBalanceAsNumbers()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        // As text these would sort "0.99" < "1000.0" < "12.5" < "250.1".
        await SeedStatementAsync(db,
            ("A", 1000m, "credit", -5m),
            ("B", 12.5m, "debit", 250m),
            ("C", 0.99m, "debit", 10.25m),
            ("D", 250.1m, "debit", -120m));

        var ascending = await TransactionsHandler(db).HandleAsync(
            new GetTransactionsQuery(null, null, null, null, null, Take: 100, SortBy: "amount", SortDir: "asc"));
        var byBalance = await TransactionsHandler(db).HandleAsync(
            new GetTransactionsQuery(null, null, null, null, null, Take: 100, SortBy: "balance", SortDir: "desc"));

        Assert.Equal([0.99m, 12.5m, 250.1m, 1000m], ascending.Items.Select(t => t.Amount));
        Assert.Equal([250m, 10.25m, -5m, -120m], byBalance.Items.Select(t => t.Balance));
    }

    [Fact]
    public async Task GetTransactions_SumsCreditsAndDebitsInSql()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        await SeedStatementAsync(db,
            ("SALARY", 1500.10m, "credit", 1500.10m),
            ("REFUND", 0.2m, "credit", 1500.30m),
            ("RENT", 700.05m, "debit", 800.25m),
            ("GROCERIES", 45.99m, "debit", 754.26m));

        var result = await TransactionsHandler(db).HandleAsync(
            new GetTransactionsQuery(null, null, null, null, null, Take: 100));

        Assert.Equal(1500.30m, result.TotalCredit);
        Assert.Equal(746.04m, result.TotalDebit);
    }

    [Theory]
    [InlineData("serviços")]
    [InlineData("SERVIÇOS")]
    [InlineData("água")]
    [InlineData("pagamento")]
    public async Task GetTransactions_SearchIgnoresCaseIncludingAccentedLetters(string search)
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        await SeedStatementAsync(db,
            ("PAGAMENTO SERVIÇOS ÁGUA", 20m, "debit", 100m),
            ("COMPRA CONTINENTE", 30m, "debit", 70m));

        var result = await TransactionsHandler(db).HandleAsync(
            new GetTransactionsQuery(null, null, null, null, search, Take: 100));

        Assert.Equal("PAGAMENTO SERVIÇOS ÁGUA", Assert.Single(result.Items).Description);
    }

    [Fact]
    public async Task GetGroceryItems_SumsAndSortsAmountsInSqlAndMatchesStoreIgnoringCase()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        db.GroceryReceipts.Add(new GroceryReceipt
        {
            StoreName = "Continente",
            ReceiptDate = new DateOnly(2026, 1, 10),
            Total = 14.24m,
            Items =
            [
                new GroceryItem { Description = "BANANA", Amount = 1.99m },
                new GroceryItem { Description = "AZEITE", Amount = 11.5m },
                new GroceryItem { Description = "PÃO", Amount = 0.75m },
            ],
        });
        await db.SaveChangesAsync();

        var result = await new GetGroceryItemsQueryHandler(db, NullLogger<GetGroceryItemsQueryHandler>.Instance)
            .HandleAsync(new GetGroceryItemsQuery(null, "continente", null, null, null, "amount", "desc", Take: 100));

        Assert.Equal(14.24m, result.TotalAmount);
        Assert.Equal([11.5m, 1.99m, 0.75m], result.Items.Select(i => i.Amount));
    }

    [Fact]
    public async Task GetGroceryReceipts_MatchesStoreIgnoringCase()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        db.GroceryReceipts.Add(new GroceryReceipt { StoreName = "Pingo Doce", ReceiptDate = new DateOnly(2026, 1, 3), Total = 5m });
        await db.SaveChangesAsync();

        var result = await new GetGroceryReceiptsQueryHandler(db, NullLogger<GetGroceryReceiptsQueryHandler>.Instance)
            .HandleAsync("PINGO");

        Assert.Single(result);
    }

    [Fact]
    public async Task CreateInvestmentLot_ChecksASellAgainstHoldingsSummedInSql()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "VWCE", Name = "Vanguard FTSE All-World" };
        db.InvestmentAssets.Add(asset);
        await db.SaveChangesAsync();
        var handler = new CreateInvestmentLotCommandHandler(db, TestPricing.Queue());
        await handler.HandleAsync(new CreateInvestmentLotCommand(asset.Id, new DateOnly(2026, 1, 5), 0.031295m, 97.81m, null, null));
        await handler.HandleAsync(new CreateInvestmentLotCommand(asset.Id, new DateOnly(2026, 2, 5), 0.5m, 99.2m, null, null));

        var (tooMuch, error) = await handler.HandleAsync(
            new CreateInvestmentLotCommand(asset.Id, new DateOnly(2026, 3, 5), -0.531296m, 101m, null, null));
        var (exact, _) = await handler.HandleAsync(
            new CreateInvestmentLotCommand(asset.Id, new DateOnly(2026, 3, 5), -0.531295m, 101m, null, null));

        Assert.Null(tooMuch);
        Assert.NotNull(error);
        Assert.NotNull(exact);
    }

    [Fact]
    public async Task GetCategories_SortsAccentedLettersBesideTheirBaseLetter()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        foreach (var name in new[] { "Papel", "Banana", "Pão", "apple", "Água", "Casa" })
            db.Categories.Add(new Category { Name = name });
        await db.SaveChangesAsync();

        var result = await new GetCategoriesQueryHandler(db, NullLogger<GetCategoriesQueryHandler>.Instance).HandleAsync();

        // NOCASE alone would put "Água" last and "Pão" after "Papel".
        Assert.Equal(["Água", "apple", "Banana", "Casa", "Pão", "Papel"], result.Select(c => c.Name));
    }

    [Fact]
    public async Task UniqueNames_IgnoreCase()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        db.Categories.Add(new Category { Name = "Food" });
        await db.SaveChangesAsync();

        Assert.True(await db.Categories.AnyAsync(c => c.Name == "FOOD"));

        db.Categories.Add(new Category { Name = "food" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_HoldsDecimalsToTheirColumnScale()
    {
        using var database = new SqliteTestDatabase();
        await using (var db = database.CreateContext())
        {
            var statement = await SeedStatementAsync(db,
                ("UP", 12.345m, "credit", -12.345m),
                ("KEPT", 0.1m, "debit", 99999999.99m));
            var asset = new InvestmentAsset { AssetType = "ETF", Ticker = "VWCE", Name = "Vanguard FTSE All-World" };
            asset.Lots.Add(new InvestmentLot { Date = new DateOnly(2026, 1, 5), Quantity = 0.0312955m, PricePerUnit = 97.81235m });
            db.InvestmentAssets.Add(asset);
            await db.SaveChangesAsync();
        }

        await using var verify = database.CreateContext();
        var transactions = await verify.Transactions.OrderBy(t => t.Id).ToListAsync();
        var lot = await verify.InvestmentLots.SingleAsync();

        Assert.Equal(12.35m, transactions[0].Amount);
        Assert.Equal(-12.35m, transactions[0].Balance);
        Assert.Equal(0.1m, transactions[1].Amount);
        Assert.Equal(99999999.99m, transactions[1].Balance);
        Assert.Equal(0.031296m, lot.Quantity);
        Assert.Equal(97.8124m, lot.PricePerUnit);
    }

    [Fact]
    public async Task ForeignKeys_AreEnforced()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var profile = new SalaryProfile { Name = "Acme" };
        var itemCategory = new SalaryItemCategory { Name = "Base", ItemType = "income", SalaryProfile = profile };
        db.SalarySlips.Add(new SalarySlip
        {
            SalaryProfile = profile,
            Period = new DateOnly(2026, 1, 1),
            GrossAmount = 1000m,
            NetAmount = 800m,
            LineItems = [new SalaryLineItem { SalaryItemCategory = itemCategory, Amount = 1000m }],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Line items restrict deleting the category they use.
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.SalaryItemCategories.Remove(await db.SalaryItemCategories.SingleAsync());
            await db.SaveChangesAsync();
        });
    }
}
