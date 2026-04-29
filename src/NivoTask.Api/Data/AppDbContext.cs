using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Models;

namespace NivoTask.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // CRITICAL: Identity tables need this

        builder.Entity<Board>(e =>
        {
            e.HasOne(b => b.User)
             .WithMany()
             .HasForeignKey(b => b.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(b => b.Columns)
             .WithOne(c => c.Board)
             .HasForeignKey(c => c.BoardId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BoardColumn>(e =>
        {
            e.Property(c => c.IsDone).HasDefaultValue(false);

            e.HasMany(c => c.Tasks)
             .WithOne(t => t.Column)
             .HasForeignKey(t => t.ColumnId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskItem>(e =>
        {
            e.HasOne(t => t.ParentTask)
             .WithMany(t => t.SubTasks)
             .HasForeignKey(t => t.ParentTaskId)
             .OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false);
        });

        builder.Entity<Label>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(50);
            e.Property(l => l.Color).HasMaxLength(20);

            e.HasOne(l => l.Board)
             .WithMany(b => b.Labels)
             .HasForeignKey(l => l.BoardId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskLabel>(e =>
        {
            e.HasKey(tl => new { tl.TaskId, tl.LabelId });

            e.HasOne(tl => tl.Task)
             .WithMany(t => t.TaskLabels)
             .HasForeignKey(tl => tl.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(tl => tl.Label)
             .WithMany(l => l.TaskLabels)
             .HasForeignKey(tl => tl.LabelId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ActivityEntry>(e =>
        {
            e.Property(a => a.Detail).HasMaxLength(500);

            e.HasOne(a => a.Task)
             .WithMany()
             .HasForeignKey(a => a.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(a => new { a.TaskId, a.CreatedAt });
        });

        builder.Entity<TimeEntry>(e =>
        {
            // Task is now optional — free entries (board-level) have TaskId = null.
            // SetNull on task delete preserves the entry against the board total.
            e.HasOne(te => te.Task)
             .WithMany(t => t.TimeEntries)
             .HasForeignKey(te => te.TaskId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);

            // Board is required and cascades — deleting a board removes all its entries.
            e.HasOne(te => te.Board)
             .WithMany()
             .HasForeignKey(te => te.BoardId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(te => te.BoardId)
             .HasDatabaseName("IX_TimeEntries_BoardId");

            e.HasOne(te => te.User)
             .WithMany()
             .HasForeignKey(te => te.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // One active timer per user — MySQL doesn't support filtered indexes.
            // Use a stored generated column that is non-null only for active timers,
            // then unique index on (UserId, ActiveTimerFlag). MySQL skips NULLs in unique.
            e.Property<bool?>("ActiveTimerFlag")
             .HasComputedColumnSql("CASE WHEN `StartTime` IS NOT NULL AND `EndTime` IS NULL THEN TRUE ELSE NULL END", stored: true);

            e.HasIndex("UserId", "ActiveTimerFlag")
             .IsUnique()
             .HasDatabaseName("IX_TimeEntries_ActiveTimer");
        });
    }
}
