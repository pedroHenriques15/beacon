using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Api.Migrations
{
    /// <summary>
    /// Data-only migration. "Internal Transfer" was superseded by "Excluded" when the
    /// IsInternalTransfer column was renamed, but the category itself was still being seeded.
    /// This folds it into "Excluded" and makes the Excluded label authoritative for the
    /// IsExcluded flag, so labelled-but-still-counted transactions stop showing up in
    /// income/spending totals and analytics.
    /// </summary>
    /// <inheritdoc />
    public partial class MergeInternalTransferIntoExcluded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Name] = N'Excluded')
                    INSERT INTO [Categories] ([Name], [Color], [IsProtected])
                    VALUES (N'Excluded', N'#64748b', 1);

                DECLARE @excludedId INT = (SELECT TOP 1 [Id] FROM [Categories] WHERE [Name] = N'Excluded');
                DECLARE @internalId INT = (SELECT TOP 1 [Id] FROM [Categories] WHERE [Name] = N'Internal Transfer');

                IF @internalId IS NOT NULL
                BEGIN
                    UPDATE [CategoryRules] SET [CategoryId] = @excludedId WHERE [CategoryId] = @internalId;
                    UPDATE [Transactions]  SET [CategoryId] = @excludedId WHERE [CategoryId] = @internalId;
                    DELETE FROM [Categories] WHERE [Id] = @internalId;
                END

                UPDATE [Transactions]
                SET [IsExcluded] = 1
                WHERE [CategoryId] = @excludedId AND [IsExcluded] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreates the empty category only - which transactions were on it, and which of them
            // were already excluded beforehand, is not recoverable.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Name] = N'Internal Transfer')
                    INSERT INTO [Categories] ([Name], [Color], [IsProtected])
                    VALUES (N'Internal Transfer', N'#64748b', 1);
                """);
        }
    }
}
