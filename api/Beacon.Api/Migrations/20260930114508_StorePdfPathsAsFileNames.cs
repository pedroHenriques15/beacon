using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <summary>
    /// Data-only migration. <c>PdfPath</c> held the absolute path the upload wrote, which tied the
    /// database to one machine's storage root; it now holds only the file name (<c>&lt;guid&gt;.pdf</c>),
    /// which <c>FileStorageService</c> resolves against <c>Storage__Path</c>. Keeps the part after the
    /// last <c>/</c> or <c>\</c>. Down does nothing: the old directories are neither needed nor
    /// recoverable.
    /// </summary>
    /// <inheritdoc />
    public partial class StorePdfPathsAsFileNames : Migration
    {
        private static readonly string[] Tables = ["MonthlyStatements", "SalarySlips", "GroceryReceipts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
                migrationBuilder.Sql($"""
                    UPDATE [{table}]
                    SET [PdfPath] = RIGHT([PdfPath], CHARINDEX('/', REVERSE(REPLACE([PdfPath], '\', '/'))) - 1)
                    WHERE [PdfPath] LIKE '%[/\]%' AND [PdfPath] NOT LIKE '%[/\]';
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
