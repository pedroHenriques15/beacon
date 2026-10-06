using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class FlagRowsInExcludedCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Statement and meal-card imports put rows in Excluded by rule without setting the flag
            // (ADR-006). Only ever sets it: a row excluded with no category (a savings-plan buy) or
            // with another category stays as it is. The category is found by name, so this holds
            // on any install.
            migrationBuilder.Sql(
                "UPDATE \"Transactions\" SET \"IsExcluded\" = 1 WHERE \"IsExcluded\" = 0 AND \"CategoryId\" IN " +
                "(SELECT \"Id\" FROM \"Categories\" WHERE \"Name\" = 'Excluded');");

            migrationBuilder.Sql(
                "UPDATE \"GroceryItems\" SET \"IsExcluded\" = 1 WHERE \"IsExcluded\" = 0 AND \"CategoryId\" IN " +
                "(SELECT \"Id\" FROM \"GroceryCategories\" WHERE \"Name\" = 'Excluded');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the rows it flagged can't be told apart from the rest, and a row in
            // Excluded is meant to be excluded.
        }
    }
}
