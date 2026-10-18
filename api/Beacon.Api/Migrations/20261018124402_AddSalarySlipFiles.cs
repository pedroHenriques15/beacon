using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSalarySlipFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalarySlipFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SalarySlipId = table.Column<int>(type: "INTEGER", nullable: false),
                    PdfPath = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    SourceFile = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarySlipFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalarySlipFiles_SalarySlips_SalarySlipId",
                        column: x => x.SalarySlipId,
                        principalTable: "SalarySlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalarySlipFiles_SalarySlipId",
                table: "SalarySlipFiles",
                column: "SalarySlipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalarySlipFiles");
        }
    }
}
