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

        builder.Entity<TimeEntry>(e =>
        {
            e.HasOne(te => te.Task)
             .WithMany(t => t.TimeEntries)
             .HasForeignKey(te => te.TaskId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(te => te.User)
             .WithMany()
             .HasForeignKey(te => te.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // One active timer at a time -- partial unique index
            // Only apply filtered unique index for MySQL; SQLite ignores/misapplies HasFilter
            if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                e.HasIndex(te => te.UserId)
                 .HasFilter("`EndTime` IS NULL")
                 .IsUnique()
                 .HasDatabaseName("IX_TimeEntries_ActiveTimer");
            }
        });
    }
}
