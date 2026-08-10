using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsinAndWidenLotQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvestmentLots",
                type: "decimal(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AddColumn<string>(
                name: "Isin",
                table: "InvestmentAssets",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentAssets_Isin",
                table: "InvestmentAssets",
                column: "Isin",
                unique: true,
                filter: "[Isin] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvestmentAssets_Isin",
                table: "InvestmentAssets");

            migrationBuilder.DropColumn(
                name: "Isin",
                table: "InvestmentAssets");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvestmentLots",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)");
        }
    }
}
