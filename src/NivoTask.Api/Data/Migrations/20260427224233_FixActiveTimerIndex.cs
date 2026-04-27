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
            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_ActiveTimer",
                table: "TimeEntries");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_ActiveTimer",
                table: "TimeEntries",
                column: "UserId",
                unique: true,
                filter: "`StartTime` IS NOT NULL AND `EndTime` IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_ActiveTimer",
                table: "TimeEntries");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_ActiveTimer",
                table: "TimeEntries",
                column: "UserId",
                unique: true,
                filter: "`EndTime` IS NULL");
        }
    }
}
