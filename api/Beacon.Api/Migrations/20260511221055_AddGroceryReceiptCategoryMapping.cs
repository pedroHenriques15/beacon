using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGroceryReceiptCategoryMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptCategory",
                table: "GroceryItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroceryReceiptCategoryMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptCategoryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GroceryCategoryId = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_GroceryReceiptCategoryMappings_GroceryCategoryId",
                table: "GroceryReceiptCategoryMappings",
                column: "GroceryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryReceiptCategoryMappings_ReceiptCategoryName",
                table: "GroceryReceiptCategoryMappings",
                column: "ReceiptCategoryName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroceryReceiptCategoryMappings");

            migrationBuilder.DropColumn(
                name: "ReceiptCategory",
                table: "GroceryItems");
        }
    }
}
