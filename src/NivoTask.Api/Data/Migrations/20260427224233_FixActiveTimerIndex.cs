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
            // 1. Fix any remaining ghost manual entries
            migrationBuilder.Sql(
                "UPDATE `TimeEntries` SET `EndTime` = UTC_TIMESTAMP() WHERE `StartTime` IS NULL AND `EndTime` IS NULL");

            // 2. Drop FK on UserId (MySQL may block index drop if FK uses it)
            migrationBuilder.Sql(@"
                SET @fk_name = (
                    SELECT CONSTRAINT_NAME
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TimeEntries'
                      AND COLUMN_NAME = 'UserId'
                      AND REFERENCED_TABLE_NAME = 'AspNetUsers'
                    LIMIT 1
                );
                SET @sql = IF(@fk_name IS NOT NULL,
                    CONCAT('ALTER TABLE `TimeEntries` DROP FOREIGN KEY `', @fk_name, '`'),
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");

            // 3. Drop old filtered unique index if it exists
            migrationBuilder.Sql(@"
                SET @idx_exists = (
                    SELECT COUNT(*) FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TimeEntries'
                      AND INDEX_NAME = 'IX_TimeEntries_ActiveTimer'
                );
                SET @sql = IF(@idx_exists > 0,
                    'ALTER TABLE `TimeEntries` DROP INDEX `IX_TimeEntries_ActiveTimer`',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");

            // 4. Add stored generated column: non-null only when timer is active
            migrationBuilder.Sql(
                "ALTER TABLE `TimeEntries` ADD COLUMN `ActiveTimerFlag` tinyint(1) AS " +
                "(CASE WHEN `StartTime` IS NOT NULL AND `EndTime` IS NULL THEN TRUE ELSE NULL END) STORED");

            // 5. Create unique index on (UserId, ActiveTimerFlag) — MySQL skips NULLs
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX `IX_TimeEntries_ActiveTimer` ON `TimeEntries` (`UserId`, `ActiveTimerFlag`)");

            // 6. Re-add FK constraint
            migrationBuilder.Sql(
                "ALTER TABLE `TimeEntries` ADD CONSTRAINT `FK_TimeEntries_AspNetUsers_UserId` " +
                "FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop FK
            migrationBuilder.Sql(@"
                SET @fk_name = (
                    SELECT CONSTRAINT_NAME
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TimeEntries'
                      AND COLUMN_NAME = 'UserId'
                      AND REFERENCED_TABLE_NAME = 'AspNetUsers'
                    LIMIT 1
                );
                SET @sql = IF(@fk_name IS NOT NULL,
                    CONCAT('ALTER TABLE `TimeEntries` DROP FOREIGN KEY `', @fk_name, '`'),
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");

            // Drop new index
            migrationBuilder.Sql(@"
                SET @idx_exists = (
                    SELECT COUNT(*) FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'TimeEntries'
                      AND INDEX_NAME = 'IX_TimeEntries_ActiveTimer'
                );
                SET @sql = IF(@idx_exists > 0,
                    'ALTER TABLE `TimeEntries` DROP INDEX `IX_TimeEntries_ActiveTimer`',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");

            // Drop generated column
            migrationBuilder.Sql("ALTER TABLE `TimeEntries` DROP COLUMN `ActiveTimerFlag`");

            // Restore original filtered index (will fail on MySQL but matches old model)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX `IX_TimeEntries_ActiveTimer` ON `TimeEntries` (`UserId`)");

            // Re-add FK
            migrationBuilder.Sql(
                "ALTER TABLE `TimeEntries` ADD CONSTRAINT `FK_TimeEntries_AspNetUsers_UserId` " +
                "FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE");
        }
    }
}
