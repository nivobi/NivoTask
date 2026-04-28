using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using NivoTask.Api.Data;
using NivoTask.Api.Models;
using NivoTask.Shared.Dtos.Setup;

namespace NivoTask.Api.Controllers;

[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private static readonly SemaphoreSlim _completeLock = new(1, 1);

    private readonly IWebHostEnvironment _env;
    private readonly IHostApplicationLifetime _lifetime;

    public SetupController(IWebHostEnvironment env, IHostApplicationLifetime lifetime)
    {
        _env = env;
        _lifetime = lifetime;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new SetupStatusResponse { IsSetupComplete = IsSetupComplete() });
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
    {
        if (IsSetupComplete())
            return NotFound();

        var connectionString = BuildConnectionString(request.Server, request.Port, request.Database,
            request.Username, request.Password);

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteSetup([FromBody] CompleteSetupRequest request)
    {
        if (IsSetupComplete())
            return NotFound();

        if (!await _completeLock.WaitAsync(TimeSpan.Zero))
            return Conflict(new { error = "Setup is already in progress." });

        try
        {
            var connectionString = BuildConnectionString(request.Server, request.Port, request.Database,
                request.DbUsername, request.DbPassword);

            // 1. Validate connection
            try
            {
                await using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Database connection failed: {ex.Message}" });
            }

            // 2. Write setup.json with SetupComplete = false (crash safety)
            var setupPath = GetSetupFilePath();
            var setupJson = new
            {
                SetupComplete = false,
                ConnectionStrings = new { DefaultConnection = connectionString }
            };
            await System.IO.File.WriteAllTextAsync(setupPath,
                JsonSerializer.Serialize(setupJson, new JsonSerializerOptions { WriteIndented = true }));

            // 3. Run EF Core migrations via ad-hoc context
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            await using var db = new AppDbContext(optionsBuilder.Options);
            await db.Database.MigrateAsync();

            // 4. Create admin user via ad-hoc Identity
            var tempServices = new ServiceCollection();
            tempServices.AddLogging();
            tempServices.AddDbContext<AppDbContext>(o =>
                o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
            tempServices.AddIdentityCore<AppUser>(opts =>
                {
                    opts.Password.RequireDigit = false;
                    opts.Password.RequireLowercase = false;
                    opts.Password.RequireUppercase = false;
                    opts.Password.RequireNonAlphanumeric = false;
                    opts.Password.RequiredLength = 8;
                    opts.SignIn.RequireConfirmedEmail = false;
                    opts.SignIn.RequireConfirmedAccount = false;
                })
                .AddEntityFrameworkStores<AppDbContext>();

            await using var tempProvider = tempServices.BuildServiceProvider();
            var userManager = tempProvider.GetRequiredService<UserManager<AppUser>>();

            var user = new AppUser
            {
                UserName = request.AdminEmail,
                Email = request.AdminEmail,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(user, request.AdminPassword);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    error = $"Failed to create admin: {string.Join(", ", result.Errors.Select(e => e.Description))}"
                });
            }

            // 5. Optionally create first board with default columns
            if (!string.IsNullOrWhiteSpace(request.BoardName))
            {
                // Use a fresh context to get the user ID
                await using var dbForBoard = new AppDbContext(optionsBuilder.Options);
                var createdUser = await dbForBoard.Users.FirstAsync(u => u.Email == request.AdminEmail);
                var board = new Board
                {
                    Name = request.BoardName,
                    UserId = createdUser.Id,
                    Columns =
                    [
                        new BoardColumn { Name = "To Do", SortOrder = 0 },
                        new BoardColumn { Name = "In Progress", SortOrder = 1 },
                        new BoardColumn { Name = "Done", SortOrder = 2, IsDone = true }
                    ]
                };
                dbForBoard.Boards.Add(board);
                await dbForBoard.SaveChangesAsync();
            }

            // 6. Mark setup complete
            var finalJson = new
            {
                SetupComplete = true,
                ConnectionStrings = new { DefaultConnection = connectionString }
            };
            await System.IO.File.WriteAllTextAsync(setupPath,
                JsonSerializer.Serialize(finalJson, new JsonSerializerOptions { WriteIndented = true }));

            // 7. Trigger app shutdown — hosting infra (IIS/Windows Service) auto-restarts
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500); // Give response time to flush
                _lifetime.StopApplication();
            });

            return Ok(new { success = true, message = "Setup complete. Application is restarting..." });
        }
        finally
        {
            _completeLock.Release();
        }
    }

    private bool IsSetupComplete()
    {
        var path = GetSetupFilePath();
        if (!System.IO.File.Exists(path)) return false;

        try
        {
            var json = System.IO.File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("SetupComplete", out var prop) && prop.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private string GetSetupFilePath() =>
        Path.Combine(_env.ContentRootPath, "setup.json");

    private static string BuildConnectionString(string server, int port, string database,
        string username, string password) =>
        $"Server={server};Port={port};Database={database};User={username};Password={password};";
}
