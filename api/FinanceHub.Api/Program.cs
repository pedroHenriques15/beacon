using FinanceHub.Api.Data;
using FinanceHub.Api.Features.Backup.Commands.CreateBackup;
using FinanceHub.Api.Features.Backup.Commands.RestoreBackup;
using FinanceHub.Api.Models;
using FinanceHub.Api.Features.Categories.Commands.CreateCategory;
using FinanceHub.Api.Features.Categories.Commands.CreateCategoryRule;
using FinanceHub.Api.Features.Categories.Commands.DeleteCategory;
using FinanceHub.Api.Features.Categories.Commands.DeleteCategoryRule;
using FinanceHub.Api.Features.Categories.Commands.UpdateCategory;
using FinanceHub.Api.Features.Categories.Commands.UpdateCategoryRule;
using FinanceHub.Api.Features.Categories.Queries.GetCategories;
using FinanceHub.Api.Features.Categories.Queries.GetCategoryRules;
using FinanceHub.Api.Features.Categories.Shared;
using FinanceHub.Api.Features.Salary.Commands.CreateSalaryItemCategory;
using FinanceHub.Api.Features.Salary.Commands.CreateSalaryProfile;
using FinanceHub.Api.Features.Salary.Commands.CreateSalarySlip;
using FinanceHub.Api.Features.Salary.Commands.ParseSalarySlip;
using FinanceHub.Api.Features.Salary.Commands.DeleteSalaryItemCategory;
using FinanceHub.Api.Features.Salary.Commands.DeleteSalaryProfile;
using FinanceHub.Api.Features.Salary.Commands.DeleteSalarySlip;
using FinanceHub.Api.Features.Salary.Commands.UpdateSalaryItemCategory;
using FinanceHub.Api.Features.Salary.Commands.UpdateSalaryProfile;
using FinanceHub.Api.Features.Salary.Commands.UpdateSalarySlip;
using FinanceHub.Api.Features.Salary.Queries.GetSalaryItemCategories;
using FinanceHub.Api.Features.Salary.Queries.GetSalaryProfiles;
using FinanceHub.Api.Features.Salary.Queries.GetSalarySlips;
using FinanceHub.Api.Features.Statements.Commands.DeleteStatement;
using FinanceHub.Api.Features.Statements.Commands.ImportMealCardText;
using FinanceHub.Api.Features.Statements.Commands.UploadStatement;
using FinanceHub.Api.Features.Statements.Queries.DownloadStatementFile;
using FinanceHub.Api.Features.Statements.Queries.GetStatementById;
using FinanceHub.Api.Features.Statements.Queries.GetStatements;
using FinanceHub.Api.Features.Transactions.Commands.CreateTransaction;
using FinanceHub.Api.Features.Transactions.Commands.DeleteTransaction;
using FinanceHub.Api.Features.Transactions.Commands.MarkTransfers;
using FinanceHub.Api.Features.Transactions.Commands.SetTransactionCategory;
using FinanceHub.Api.Features.Transactions.Commands.UpdateTransaction;
using FinanceHub.Api.Features.Transactions.Queries.GetTransactions;
using FinanceHub.Api.Features.Groceries.Commands.CreateGroceryItem;
using FinanceHub.Api.Features.Groceries.Commands.DeleteGroceryItem;
using FinanceHub.Api.Features.Groceries.Commands.DeleteGroceryReceipt;
using FinanceHub.Api.Features.Groceries.Commands.SetGroceryItemCategory;
using FinanceHub.Api.Features.Groceries.Commands.UpdateGroceryItem;
using FinanceHub.Api.Features.Groceries.Commands.UploadGroceryReceipt;
using FinanceHub.Api.Features.Groceries.Queries.GetGroceryItems;
using FinanceHub.Api.Features.Groceries.Queries.GetGroceryReceipts;
using FinanceHub.Api.Features.Groceries.Shared;
using FinanceHub.Api.Features.GroceryCategories.Commands.CreateGroceryCategory;
using FinanceHub.Api.Features.GroceryCategories.Commands.CreateGroceryCategoryRule;
using FinanceHub.Api.Features.GroceryCategories.Commands.DeleteGroceryCategory;
using FinanceHub.Api.Features.GroceryCategories.Commands.DeleteGroceryCategoryRule;
using FinanceHub.Api.Features.GroceryCategories.Commands.UpdateGroceryCategory;
using FinanceHub.Api.Features.GroceryCategories.Commands.UpdateGroceryCategoryRule;
using FinanceHub.Api.Features.GroceryCategories.Commands.CreateGroceryReceiptCategoryMapping;
using FinanceHub.Api.Features.GroceryCategories.Commands.DeleteGroceryReceiptCategoryMapping;
using FinanceHub.Api.Features.GroceryCategories.Queries.GetGroceryCategories;
using FinanceHub.Api.Features.GroceryCategories.Queries.GetGroceryCategoryRules;
using FinanceHub.Api.Features.GroceryCategories.Queries.GetGroceryReceiptCategoryMappings;
using FinanceHub.Api.Features.Upload.Commands.UnifiedUploadBatch;
using FinanceHub.Api.Middleware;
using FinanceHub.Api.Services;
using FinanceHub.Api.Services.Parsing;
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
builder.Services.AddSingleton<BankStatementParserFactory>();

builder.Services.AddSingleton<IGroceryReceiptParser, ContinenteParser>();
builder.Services.AddSingleton<GroceryReceiptParserFactory>();

builder.Services.AddSingleton<ISalarySlipParser, CentralGestParser>();
builder.Services.AddSingleton<ISalarySlipParser, DomirestParser>();
builder.Services.AddSingleton<SalarySlipParserFactory>();

builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddHttpClient("google-oauth");
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<PdfExtractorService>();
builder.Services.AddScoped<StatementUploadService>();

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

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()));

var app = builder.Build();

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
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

await SeedDefaultDataAsync(app);

app.Run();

static async Task SeedDefaultDataAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!db.Categories.Any(c => c.Name == "Internal Transfer"))
        db.Categories.Add(new Category { Name = "Internal Transfer", Color = "#64748b", IsProtected = true });

    await db.SaveChangesAsync();
}
