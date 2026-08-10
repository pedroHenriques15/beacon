using Beacon.Api.Data;
using Beacon.Api.Features.Backup.Commands.CreateBackup;
using Beacon.Api.Features.Backup.Commands.RestoreBackup;
using Beacon.Api.Features.Backup.Queries.DownloadBackup;
using Beacon.Api.Models;
using Beacon.Api.Features.Categories.Commands.CreateCategory;
using Beacon.Api.Features.Categories.Commands.CreateCategoryRule;
using Beacon.Api.Features.Categories.Commands.DeleteCategory;
using Beacon.Api.Features.Categories.Commands.DeleteCategoryRule;
using Beacon.Api.Features.Categories.Commands.UpdateCategory;
using Beacon.Api.Features.Categories.Commands.UpdateCategoryRule;
using Beacon.Api.Features.Categories.Queries.GetCategories;
using Beacon.Api.Features.Categories.Queries.GetCategoryRules;
using Beacon.Api.Features.Categories.Shared;
using Beacon.Api.Features.Salary.Commands.CreateSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.CreateSalaryProfile;
using Beacon.Api.Features.Salary.Commands.CreateSalarySlip;
using Beacon.Api.Features.Salary.Commands.ParseSalarySlip;
using Beacon.Api.Features.Salary.Commands.DeleteSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.DeleteSalaryProfile;
using Beacon.Api.Features.Salary.Commands.DeleteSalarySlip;
using Beacon.Api.Features.Salary.Commands.UpdateSalaryItemCategory;
using Beacon.Api.Features.Salary.Commands.UpdateSalaryProfile;
using Beacon.Api.Features.Salary.Commands.UpdateSalarySlip;
using Beacon.Api.Features.Salary.Queries.GetSalaryItemCategories;
using Beacon.Api.Features.Salary.Queries.GetSalaryProfiles;
using Beacon.Api.Features.Salary.Queries.GetSalarySlips;
using Beacon.Api.Features.Investments.Queries.GetInvestmentAssets;
using Beacon.Api.Features.Investments.Commands.CreateInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.UpdateInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentAsset;
using Beacon.Api.Features.Investments.Commands.CreateInvestmentLot;
using Beacon.Api.Features.Investments.Commands.UpdateInvestmentLot;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentLot;
using Beacon.Api.Features.Investments.Commands.UpsertInvestmentPrice;
using Beacon.Api.Features.Investments.Commands.DeleteInvestmentPriceSnapshot;
using Beacon.Api.Features.Investments.Commands.FetchInvestmentPrice;
using Beacon.Api.Features.Investments.Commands.BackfillPriceHistory;
using Beacon.Api.Features.Investments.Shared;
using Beacon.Api.Features.Statements.Commands.DeleteStatement;
using Beacon.Api.Features.Statements.Commands.ImportMealCardText;
using Beacon.Api.Features.Statements.Commands.UploadStatement;
using Beacon.Api.Features.Statements.Queries.DownloadStatementFile;
using Beacon.Api.Features.Statements.Queries.GetStatementById;
using Beacon.Api.Features.Statements.Queries.GetStatements;
using Beacon.Api.Features.Transactions.Commands.BulkDeleteTransactions;
using Beacon.Api.Features.Transactions.Commands.CreateTransaction;
using Beacon.Api.Features.Transactions.Commands.DeleteTransaction;
using Beacon.Api.Features.Transactions.Commands.MarkTransfers;
using Beacon.Api.Features.Transactions.Commands.SetTransactionCategory;
using Beacon.Api.Features.Transactions.Commands.UpdateTransaction;
using Beacon.Api.Features.Transactions.Queries.GetTransactions;
using Beacon.Api.Features.Groceries.Commands.CreateGroceryItem;
using Beacon.Api.Features.Groceries.Commands.DeleteGroceryItem;
using Beacon.Api.Features.Groceries.Commands.DeleteGroceryReceipt;
using Beacon.Api.Features.Groceries.Commands.MarkGroceryItemsExcluded;
using Beacon.Api.Features.Groceries.Commands.SetGroceryItemCategory;
using Beacon.Api.Features.Groceries.Commands.UpdateGroceryItem;
using Beacon.Api.Features.Groceries.Commands.UploadGroceryReceipt;
using Beacon.Api.Features.Groceries.Queries.GetGroceryItems;
using Beacon.Api.Features.Groceries.Queries.GetGroceryReceipts;
using Beacon.Api.Features.Groceries.Shared;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryCategoryRule;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryCategoryRule;
using Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;
using Beacon.Api.Features.GroceryCategories.Commands.UpdateGroceryCategoryRule;
using Beacon.Api.Features.GroceryCategories.Commands.CreateGroceryReceiptCategoryMapping;
using Beacon.Api.Features.GroceryCategories.Commands.DeleteGroceryReceiptCategoryMapping;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategories;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryCategoryRules;
using Beacon.Api.Features.GroceryCategories.Queries.GetGroceryReceiptCategoryMappings;
using Beacon.Api.Features.Upload.Commands.UnifiedUploadBatch;
using Beacon.Api.Middleware;
using Beacon.Api.Services;
using Beacon.Api.Services.Parsing;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

builder.Services.AddSingleton<IBankStatementParser, ActivoBankParser>();
builder.Services.AddSingleton<IBankStatementParser, BpiParser>();
builder.Services.AddSingleton<IBankStatementParser, RevolutParser>();
builder.Services.AddSingleton<IBankStatementParser, TradeRepublicParser>();
builder.Services.AddSingleton<BankStatementParserFactory>();

builder.Services.AddSingleton<IGroceryReceiptParser, ContinenteParser>();
builder.Services.AddSingleton<GroceryReceiptParserFactory>();

builder.Services.AddSingleton<ISalarySlipParser, CentralGestParser>();
builder.Services.AddSingleton<ISalarySlipParser, DomirestParser>();
builder.Services.AddSingleton<SalarySlipParserFactory>();

builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("google-oauth")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("google-calendar")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("google-tasks")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("alpha-vantage")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<GoogleCalendarService>();
builder.Services.AddScoped<GoogleTasksService>();
builder.Services.AddScoped<IPdfExtractor, PdfExtractorService>();
builder.Services.AddScoped<StatementUploadService>();

builder.Services.AddScoped<DownloadBackupQueryHandler>();
builder.Services.AddScoped<GetStatementsQueryHandler>();
builder.Services.AddScoped<GetStatementByIdQueryHandler>();
builder.Services.AddScoped<DownloadStatementFileQueryHandler>();
builder.Services.AddScoped<UploadStatementCommandHandler>();
builder.Services.AddScoped<ImportMealCardTextCommandHandler>();
builder.Services.AddScoped<DeleteStatementCommandHandler>();

builder.Services.AddScoped<ApplyRuleService>();
builder.Services.AddScoped<GetCategoriesQueryHandler>();
builder.Services.AddScoped<GetCategoryRulesQueryHandler>();
builder.Services.AddScoped<CreateCategoryCommandHandler>();
builder.Services.AddScoped<UpdateCategoryCommandHandler>();
builder.Services.AddScoped<DeleteCategoryCommandHandler>();
builder.Services.AddScoped<CreateCategoryRuleCommandHandler>();
builder.Services.AddScoped<DeleteCategoryRuleCommandHandler>();
builder.Services.AddScoped<UpdateCategoryRuleCommandHandler>();

builder.Services.AddScoped<GetTransactionsQueryHandler>();
builder.Services.AddScoped<SetTransactionCategoryCommandHandler>();
builder.Services.AddScoped<MarkTransfersCommandHandler>();
builder.Services.AddScoped<CreateTransactionCommandHandler>();
builder.Services.AddScoped<UpdateTransactionCommandHandler>();
builder.Services.AddScoped<DeleteTransactionCommandHandler>();
builder.Services.AddScoped<BulkDeleteTransactionsCommandHandler>();

builder.Services.AddScoped<GroceryApplyRuleService>();
builder.Services.AddScoped<GroceryReceiptUploadService>();
builder.Services.AddScoped<UploadGroceryReceiptCommandHandler>();
builder.Services.AddScoped<DeleteGroceryReceiptCommandHandler>();
builder.Services.AddScoped<GetGroceryReceiptsQueryHandler>();

builder.Services.AddScoped<GetGroceryItemsQueryHandler>();
builder.Services.AddScoped<CreateGroceryItemCommandHandler>();
builder.Services.AddScoped<UpdateGroceryItemCommandHandler>();
builder.Services.AddScoped<DeleteGroceryItemCommandHandler>();
builder.Services.AddScoped<SetGroceryItemCategoryCommandHandler>();
builder.Services.AddScoped<MarkGroceryItemsExcludedCommandHandler>();

builder.Services.AddScoped<GetGroceryCategoriesQueryHandler>();
builder.Services.AddScoped<GetGroceryCategoryRulesQueryHandler>();
builder.Services.AddScoped<CreateGroceryCategoryCommandHandler>();
builder.Services.AddScoped<UpdateGroceryCategoryCommandHandler>();
builder.Services.AddScoped<DeleteGroceryCategoryCommandHandler>();
builder.Services.AddScoped<CreateGroceryCategoryRuleCommandHandler>();
builder.Services.AddScoped<UpdateGroceryCategoryRuleCommandHandler>();
builder.Services.AddScoped<DeleteGroceryCategoryRuleCommandHandler>();
builder.Services.AddScoped<GetGroceryReceiptCategoryMappingsQueryHandler>();
builder.Services.AddScoped<CreateGroceryReceiptCategoryMappingCommandHandler>();
builder.Services.AddScoped<DeleteGroceryReceiptCategoryMappingCommandHandler>();

builder.Services.AddScoped<UnifiedUploadBatchCommandHandler>();

builder.Services.AddScoped<CreateBackupCommandHandler>();
builder.Services.AddScoped<RestoreBackupCommandHandler>();

builder.Services.AddScoped<GetSalaryProfilesQueryHandler>();
builder.Services.AddScoped<CreateSalaryProfileCommandHandler>();
builder.Services.AddScoped<UpdateSalaryProfileCommandHandler>();
builder.Services.AddScoped<DeleteSalaryProfileCommandHandler>();
builder.Services.AddScoped<GetSalarySlipsQueryHandler>();
builder.Services.AddScoped<CreateSalarySlipCommandHandler>();
builder.Services.AddScoped<ParseSalarySlipCommandHandler>();
builder.Services.AddScoped<UpdateSalarySlipCommandHandler>();
builder.Services.AddScoped<DeleteSalarySlipCommandHandler>();
builder.Services.AddScoped<GetSalaryItemCategoriesQueryHandler>();
builder.Services.AddScoped<CreateSalaryItemCategoryCommandHandler>();
builder.Services.AddScoped<UpdateSalaryItemCategoryCommandHandler>();
builder.Services.AddScoped<DeleteSalaryItemCategoryCommandHandler>();

builder.Services.AddScoped<AlphaVantageService>();
builder.Services.AddScoped<GetInvestmentAssetsQueryHandler>();
builder.Services.AddScoped<CreateInvestmentAssetCommandHandler>();
builder.Services.AddScoped<UpdateInvestmentAssetCommandHandler>();
builder.Services.AddScoped<DeleteInvestmentAssetCommandHandler>();
builder.Services.AddScoped<CreateInvestmentLotCommandHandler>();
builder.Services.AddScoped<UpdateInvestmentLotCommandHandler>();
builder.Services.AddScoped<DeleteInvestmentLotCommandHandler>();
builder.Services.AddScoped<UpsertInvestmentPriceCommandHandler>();
builder.Services.AddScoped<DeleteInvestmentPriceSnapshotCommandHandler>();
builder.Services.AddScoped<FetchInvestmentPriceCommandHandler>();
builder.Services.AddScoped<BackfillPriceHistoryCommandHandler>();
builder.Services.AddScoped<SavingsPlanImportService>();
builder.Services.AddHostedService<InvestmentPriceRefreshService>();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrEmpty(app.Configuration["ApiKey"]))
        throw new InvalidOperationException(
            "ApiKey is not configured - set the ApiKey environment variable before starting.");

    if (string.IsNullOrEmpty(app.Configuration["Storage:Path"]))
        throw new InvalidOperationException(
            "Storage:Path is not configured - uploaded PDFs would land in the deploy directory " +
            "and be erased on the next deploy. Set Storage__Path to a persistent directory.");
}

if (!string.IsNullOrEmpty(app.Configuration["GoogleServices:ClientId"]))
{
    var frontendUrl = app.Configuration["GoogleServices:FrontendUrl"] ?? "";
    if (!Uri.TryCreate(frontendUrl, UriKind.Absolute, out var frontendUri) ||
        frontendUri.Scheme is not ("http" or "https"))
        throw new InvalidOperationException(
            $"GoogleServices:FrontendUrl must be an absolute http/https URL; got: '{frontendUrl}'");

    if (string.IsNullOrEmpty(app.Configuration["GoogleServices:ClientSecret"]))
        throw new InvalidOperationException(
            "GoogleServices:ClientSecret is required when GoogleServices:ClientId is set.");

    var redirectUri = app.Configuration["GoogleServices:RedirectUri"] ?? "";
    if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirectUriParsed) ||
        redirectUriParsed.Scheme is not ("http" or "https"))
        throw new InvalidOperationException(
            $"GoogleServices:RedirectUri must be an absolute http/https URL; got: '{redirectUri}'");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    if (!app.Environment.IsDevelopment())
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    await next();
});

if (app.Environment.IsDevelopment())
    app.UseCors();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

await SeedDefaultDataAsync(app);
await CleanupOrphanedPdfsAsync(app);

app.Run();

static async Task CleanupOrphanedPdfsAsync(WebApplication app)
{
    var storagePath = app.Configuration["Storage:Path"];
    if (string.IsNullOrEmpty(storagePath) || !Directory.Exists(storagePath)) return;

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    referenced.UnionWith((await db.MonthlyStatements
        .Where(s => s.PdfPath != null).Select(s => s.PdfPath!).ToListAsync()).Select(Path.GetFullPath));
    referenced.UnionWith((await db.SalarySlips
        .Where(s => s.PdfPath != null).Select(s => s.PdfPath!).ToListAsync()).Select(Path.GetFullPath));
    referenced.UnionWith((await db.GroceryReceipts
        .Where(r => r.PdfPath != null).Select(r => r.PdfPath!).ToListAsync()).Select(Path.GetFullPath));

    var cutoff = DateTime.UtcNow.AddHours(-24);
    var deleted = 0;
    foreach (var file in Directory.EnumerateFiles(storagePath, "*.pdf"))
    {
        if (referenced.Contains(Path.GetFullPath(file))) continue;
        if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
        try
        {
            File.Delete(file);
            deleted++;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete orphaned PDF {Path}", file);
        }
    }

    if (deleted > 0)
        logger.LogInformation("Deleted {Count} orphaned PDFs from storage", deleted);
}

static async Task SeedDefaultDataAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!db.Categories.Any(c => c.Name == "Internal Transfer"))
        db.Categories.Add(new Category { Name = "Internal Transfer", Color = "#64748b", IsProtected = true });

    if (!db.Categories.Any(c => c.Name == "Excluded"))
        db.Categories.Add(new Category { Name = "Excluded", Color = "#64748b", IsProtected = true });

    if (!db.GroceryCategories.Any(c => c.Name == "Excluded"))
        db.GroceryCategories.Add(new GroceryCategory { Name = "Excluded", Color = "#64748b", IsProtected = true });

    await db.SaveChangesAsync();
}
