using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class RuleMatchWholeDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every rule from before matched a part of the description, and keeps doing so.
            migrationBuilder.AddColumn<bool>(
                name: "MatchWholeDescription",
                table: "GroceryCategoryRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MatchWholeDescription",
                table: "CategoryRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchWholeDescription",
                table: "GroceryCategoryRules");

            migrationBuilder.DropColumn(
                name: "MatchWholeDescription",
                table: "CategoryRules");
        }
    }
}
