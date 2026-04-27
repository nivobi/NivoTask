using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NivoTask.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixManualEntryEndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix ghost manual entries: set EndTime on entries that have
            // StartTime = NULL and EndTime = NULL (manual entries incorrectly
            // created without EndTime, blocking the active timer unique index)
            migrationBuilder.Sql(
                "UPDATE `TimeEntries` SET `EndTime` = UTC_TIMESTAMP() WHERE `StartTime` IS NULL AND `EndTime` IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reliably revert — would need to know which entries were manual
        }
    }
}
