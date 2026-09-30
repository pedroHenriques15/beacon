using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoogleOAuthTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    RefreshToken = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, collation: "NOCASE"),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, collation: "NOCASE"),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleOAuthTokens", x => x.Id);
                    table.CheckConstraint("CK_SingleToken", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "GroceryCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroceryReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    ReceiptDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true, collation: "NOCASE"),
                    SourceFile = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    PdfPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true, collation: "NOCASE"),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Ticker = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true, collation: "NOCASE"),
                    Isin = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Bank = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, collation: "NOCASE"),
                    Account = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    PeriodFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false, collation: "NOCASE"),
                    OpeningBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SourceFile = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, collation: "NOCASE"),
                    PdfPath = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    FileHash = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    PprBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyStatements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalaryProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    HourlyRateFormula = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Value = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroceryCategoryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    Value = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryCategoryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroceryCategoryRules_GroceryCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GroceryCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroceryReceiptCategoryMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptCategoryName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    GroceryCategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryReceiptCategoryMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroceryReceiptCategoryMappings_GroceryCategories_GroceryCategoryId",
                        column: x => x.GroceryCategoryId,
                        principalTable: "GroceryCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroceryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ReceiptCategory = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true, collation: "NOCASE"),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategoryRuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategorySetManually = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroceryItems_GroceryCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "GroceryCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GroceryItems_GroceryReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "GroceryReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentLots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Fees = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentLots_InvestmentAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "InvestmentAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentPriceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentPriceSnapshots_InvestmentAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "InvestmentAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StatementId = table.Column<int>(type: "INTEGER", nullable: false),
                    DatePosting = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DateValue = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, collation: "NOCASE"),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    Balance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategoryRuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    CategorySetManually = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Transactions_MonthlyStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "MonthlyStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryItemCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalaryProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryItemCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryItemCategories_SalaryProfiles_SalaryProfileId",
                        column: x => x.SalaryProfileId,
                        principalTable: "SalaryProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalarySlips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalaryProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Period = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true, collation: "NOCASE"),
                    SourceFile = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE"),
                    PdfPath = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    FileHash = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    HoursWorked = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    HourlyRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    TotalEspecie = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarySlips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalarySlips_SalaryProfiles_SalaryProfileId",
                        column: x => x.SalaryProfileId,
                        principalTable: "SalaryProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryLineItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalarySlipId = table.Column<int>(type: "INTEGER", nullable: false),
                    SalaryItemCategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    UnitValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Percentage = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    IncidenciaBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryLineItems_SalaryItemCategories_SalaryItemCategoryId",
                        column: x => x.SalaryItemCategoryId,
                        principalTable: "SalaryItemCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalaryLineItems_SalarySlips_SalarySlipId",
                        column: x => x.SalarySlipId,
                        principalTable: "SalarySlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_CategoryId",
                table: "CategoryRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryCategories_Name",
                table: "GroceryCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroceryCategoryRules_CategoryId",
                table: "GroceryCategoryRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryItems_CategoryId",
                table: "GroceryItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryItems_ReceiptId",
                table: "GroceryItems",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryReceiptCategoryMappings_GroceryCategoryId",
                table: "GroceryReceiptCategoryMappings",
                column: "GroceryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryReceiptCategoryMappings_ReceiptCategoryName",
                table: "GroceryReceiptCategoryMappings",
                column: "ReceiptCategoryName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentAssets_Isin",
                table: "InvestmentAssets",
                column: "Isin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentLots_AssetId",
                table: "InvestmentLots",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentPriceSnapshots_AssetId_Date",
                table: "InvestmentPriceSnapshots",
                columns: new[] { "AssetId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyStatements_Bank_PeriodFrom",
                table: "MonthlyStatements",
                columns: new[] { "Bank", "PeriodFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryItemCategories_SalaryProfileId_Name",
                table: "SalaryItemCategories",
                columns: new[] { "SalaryProfileId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLineItems_SalaryItemCategoryId",
                table: "SalaryLineItems",
                column: "SalaryItemCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLineItems_SalarySlipId",
                table: "SalaryLineItems",
                column: "SalarySlipId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryProfiles_Name",
                table: "SalaryProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalarySlips_SalaryProfileId_Period",
                table: "SalarySlips",
                columns: new[] { "SalaryProfileId", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_StatementId",
                table: "Transactions",
                column: "StatementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryRules");

            migrationBuilder.DropTable(
                name: "GoogleOAuthTokens");

            migrationBuilder.DropTable(
                name: "GroceryCategoryRules");

            migrationBuilder.DropTable(
                name: "GroceryItems");

            migrationBuilder.DropTable(
                name: "GroceryReceiptCategoryMappings");

            migrationBuilder.DropTable(
                name: "InvestmentLots");

            migrationBuilder.DropTable(
                name: "InvestmentPriceSnapshots");

            migrationBuilder.DropTable(
                name: "SalaryLineItems");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "GroceryReceipts");

            migrationBuilder.DropTable(
                name: "GroceryCategories");

            migrationBuilder.DropTable(
                name: "InvestmentAssets");

            migrationBuilder.DropTable(
                name: "SalaryItemCategories");

            migrationBuilder.DropTable(
                name: "SalarySlips");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "MonthlyStatements");

            migrationBuilder.DropTable(
                name: "SalaryProfiles");
        }
    }
}
