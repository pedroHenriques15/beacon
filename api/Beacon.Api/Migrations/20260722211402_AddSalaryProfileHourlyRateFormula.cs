using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryProfileHourlyRateFormula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HourlyRateFormula",
                table: "SalaryProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "days");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HourlyRateFormula",
                table: "SalaryProfiles");
        }
    }
}
