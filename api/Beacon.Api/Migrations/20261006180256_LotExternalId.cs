using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class LotExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "InvestmentLots",
                type: "TEXT",
                maxLength: 100,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentLots_ExternalId",
                table: "InvestmentLots",
                column: "ExternalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvestmentLots_ExternalId",
                table: "InvestmentLots");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "InvestmentLots");
        }
    }
}
