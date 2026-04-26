using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NivoTask.Api.Data;

/// <summary>
/// Design-time factory for EF Core CLI tools (migrations).
/// Uses a fixed MySQL server version to avoid requiring a live DB connection.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Use a dummy connection string -- migrations don't need a live connection.
        // Specify MySQL 8.0 explicitly to avoid ServerVersion.AutoDetect needing a connection.
        optionsBuilder.UseMySql(
            "Server=localhost;Database=nivotask_design;User=root;Password=;",
            new MySqlServerVersion(new Version(8, 0, 0)));

        return new AppDbContext(optionsBuilder.Options);
    }
}
