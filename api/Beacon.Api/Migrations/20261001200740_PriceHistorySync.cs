using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class PriceHistorySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "InvestmentPriceSnapshots",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                // Existing rows mix fetched intraday quotes and hand-entered prices, which can't
                // be told apart: the first sync replaces them with closes.
                defaultValue: "Legacy",
                collation: "NOCASE");

            migrationBuilder.AddColumn<string>(
                name: "PriceSyncError",
                table: "InvestmentAssets",
                type: "TEXT",
                maxLength: 500,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<string>(
                name: "PricesSymbol",
                table: "InvestmentAssets",
                type: "TEXT",
                maxLength: 20,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<DateTime>(
                name: "PricesSyncedAt",
                table: "InvestmentAssets",
                type: "TEXT",
                nullable: true);

            // Alpha Vantage's Xetra suffix (VWCE.DEX) becomes the price source's (VWCE.DE).
            migrationBuilder.Sql(
                "UPDATE \"InvestmentAssets\" SET \"Ticker\" = substr(\"Ticker\", 1, length(\"Ticker\") - 1) " +
                "WHERE \"Ticker\" LIKE '%.DEX';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"InvestmentAssets\" SET \"Ticker\" = \"Ticker\" || 'X' WHERE \"Ticker\" LIKE '%.DE';");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "InvestmentPriceSnapshots");

            migrationBuilder.DropColumn(
                name: "PriceSyncError",
                table: "InvestmentAssets");

            migrationBuilder.DropColumn(
                name: "PricesSymbol",
                table: "InvestmentAssets");

            migrationBuilder.DropColumn(
                name: "PricesSyncedAt",
                table: "InvestmentAssets");
        }
    }
}
