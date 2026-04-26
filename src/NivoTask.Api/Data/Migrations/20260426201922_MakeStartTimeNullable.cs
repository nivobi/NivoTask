using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NivoTask.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeStartTimeNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "StartTime",
                table: "TimeEntries",
                type: "datetime(6)",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");
        }

        /// <inheritdoc />
        /// <remarks>
        /// WARNING: Data-lossy rollback. Manual entries (StartTime IS NULL) will receive
        /// DateTime.MinValue (0001-01-01) as their StartTime, making them indistinguishable
        /// from timer entries and breaking the IsManual detection logic (StartTime == null).
        /// If rolling back in production, first UPDATE or DELETE rows where StartTime IS NULL.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "StartTime",
                table: "TimeEntries",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);
        }
    }
}
