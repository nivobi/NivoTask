using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NivoTask.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixActiveTimerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL won't drop an index used by a FK. The UserId FK index is shared.
            // Fix ghost manual entries first, then recreate the filtered index with raw SQL.

            // 1. Fix any remaining ghost manual entries
            migrationBuilder.Sql(
                "UPDATE `TimeEntries` SET `EndTime` = UTC_TIMESTAMP() WHERE `StartTime` IS NULL AND `EndTime` IS NULL");

            // 2. Drop the old filtered unique index and recreate with correct filter
            //    Must drop FK constraint first since MySQL uses the index for FK
            migrationBuilder.Sql("ALTER TABLE `TimeEntries` DROP FOREIGN KEY `FK_TimeEntries_AspNetUsers_UserId`");
            migrationBuilder.Sql("ALTER TABLE `TimeEntries` DROP INDEX `IX_TimeEntries_ActiveTimer`");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX `IX_TimeEntries_ActiveTimer` ON `TimeEntries` (`UserId`) " +
                "WHERE (`StartTime` IS NOT NULL AND `EndTime` IS NULL)");
            migrationBuilder.Sql(
                "ALTER TABLE `TimeEntries` ADD CONSTRAINT `FK_TimeEntries_AspNetUsers_UserId` " +
                "FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `TimeEntries` DROP FOREIGN KEY `FK_TimeEntries_AspNetUsers_UserId`");
            migrationBuilder.Sql("ALTER TABLE `TimeEntries` DROP INDEX `IX_TimeEntries_ActiveTimer`");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX `IX_TimeEntries_ActiveTimer` ON `TimeEntries` (`UserId`) " +
                "WHERE (`EndTime` IS NULL)");
            migrationBuilder.Sql(
                "ALTER TABLE `TimeEntries` ADD CONSTRAINT `FK_TimeEntries_AspNetUsers_UserId` " +
                "FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE");
        }
    }
}
