using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NivoTask.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardScopedTimeEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Tasks_TaskId",
                table: "TimeEntries");

            migrationBuilder.AlterColumn<int>(
                name: "TaskId",
                table: "TimeEntries",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "BoardId",
                table: "TimeEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill BoardId from existing TimeEntry -> Task -> Column -> Board chain.
            // Pre-migration data has TaskId NOT NULL on every row, so this fills all rows.
            migrationBuilder.Sql(@"
                UPDATE `TimeEntries` te
                JOIN `Tasks` t ON te.`TaskId` = t.`Id`
                JOIN `BoardColumns` bc ON t.`ColumnId` = bc.`Id`
                SET te.`BoardId` = bc.`BoardId`
                WHERE te.`BoardId` = 0
            ");

            // Safety: any orphaned entries (TaskId points to deleted task) get cleaned.
            // Should be impossible with old cascade, but guard against bad state.
            migrationBuilder.Sql(@"
                DELETE FROM `TimeEntries` WHERE `BoardId` = 0
            ");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_BoardId",
                table: "TimeEntries",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Boards_BoardId",
                table: "TimeEntries",
                column: "BoardId",
                principalTable: "Boards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Tasks_TaskId",
                table: "TimeEntries",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Boards_BoardId",
                table: "TimeEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Tasks_TaskId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_BoardId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "BoardId",
                table: "TimeEntries");

            migrationBuilder.AlterColumn<int>(
                name: "TaskId",
                table: "TimeEntries",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Tasks_TaskId",
                table: "TimeEntries",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
