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

        // Short connect timeout so the request returns fast even if the MySQL host
        // is unreachable. Without this, IIS will return 502 before our catch fires.
        var connectionString = BuildConnectionString(request.Server, request.Port, request.Database,
            request.Username, request.Password) + "Connect Timeout=8;";

        var response = new TestConnectionResponse();

        // Hard ceiling on total time so an unresponsive host can't trigger an IIS proxy timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cts.Token);
            response.Success = true;

            try
            {
                await using var tableCmd = connection.CreateCommand();
                tableCmd.CommandText =
                    "SELECT COUNT(*) FROM information_schema.TABLES " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AspNetUsers'";
                var tableExists = Convert.ToInt32(await tableCmd.ExecuteScalarAsync(cts.Token)) > 0;
                response.MigrationsApplied = tableExists;

                if (tableExists)
                {
                    await using var userCmd = connection.CreateCommand();
                    userCmd.CommandText = "SELECT Email FROM AspNetUsers ORDER BY Id LIMIT 1";
                    var firstEmail = await userCmd.ExecuteScalarAsync(cts.Token) as string;
                    response.HasExistingUser = !string.IsNullOrEmpty(firstEmail);
                    response.ExistingUserEmail = firstEmail;
                }
            }
            catch
            {
                // Swallow — fall through with defaults (fresh-flow path).
            }
        }
        catch (OperationCanceledException)
        {
            response.Success = false;
            response.Error = $"Connection to {request.Server}:{request.Port} timed out after 10s. Check host/port and that your hosting provider allows outbound TCP to MySQL.";
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = $"{ex.GetType().Name}: {ex.Message}";
        }

        // Mirror the result to a flat log file in install dir so the user can read it via FTP
        // when IIS swallows the response (rare, but happens on hardened shared hosting).
        try
        {
            var logPath = Path.Combine(_env.ContentRootPath, "setup-debug.log");
            var line = $"{DateTime.UtcNow:O} test-connection success={response.Success} error={response.Error}\n";
            await System.IO.File.AppendAllTextAsync(logPath, line);
        }
        catch { /* logging is best-effort */ }

        return Ok(response);
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

            // 1. Validate connection (with short timeout so IIS doesn't 502 us first)
            connectionString += "Connect Timeout=8;";
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    await using var connection = new MySqlConnection(connectionString);
                    await connection.OpenAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return BadRequest(new { error = $"Database connection to {request.Server}:{request.Port} timed out. Check the host/port and that outbound MySQL is allowed from your host." });
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = $"Database connection failed: {ex.GetType().Name}: {ex.Message}" });
                }
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

    [HttpPost("adopt-existing")]
    public async Task<IActionResult> AdoptExisting([FromBody] AdoptExistingRequest request)
    {
        if (IsSetupComplete())
            return NotFound();

        if (!await _completeLock.WaitAsync(TimeSpan.Zero))
            return Conflict(new { error = "Setup is already in progress." });

        try
        {
            var connectionString = BuildConnectionString(request.Server, request.Port, request.Database,
                request.DbUsername, request.DbPassword);

            // 1. Validate connection + verify DB really has an existing admin user
            try
            {
                await using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                await using var tableCmd = connection.CreateCommand();
                tableCmd.CommandText =
                    "SELECT COUNT(*) FROM information_schema.TABLES " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AspNetUsers'";
                var tableExists = Convert.ToInt32(await tableCmd.ExecuteScalarAsync()) > 0;
                if (!tableExists)
                    return BadRequest(new { error = "Database has no NivoTask schema. Use the standard setup flow instead." });

                await using var userCmd = connection.CreateCommand();
                userCmd.CommandText = "SELECT COUNT(*) FROM AspNetUsers";
                var userCount = Convert.ToInt32(await userCmd.ExecuteScalarAsync());
                if (userCount < 1)
                    return BadRequest(new { error = "Database has no admin user. Use the standard setup flow instead." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Database connection failed: {ex.Message}" });
            }

            // 2. Write setup.json with SetupComplete = false (crash safety)
            var setupPath = GetSetupFilePath();
            var pendingJson = new
            {
                SetupComplete = false,
                ConnectionStrings = new { DefaultConnection = connectionString }
            };
            await System.IO.File.WriteAllTextAsync(setupPath,
                JsonSerializer.Serialize(pendingJson, new JsonSerializerOptions { WriteIndented = true }));

            // 3. Apply any pending migrations (e.g. AddBoardScopedTimeEntries on a redeploy)
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                await using var db = new AppDbContext(optionsBuilder.Options);
                await db.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Migration failed: {ex.Message}" });
            }

            // 4. Mark setup complete
            var finalJson = new
            {
                SetupComplete = true,
                ConnectionStrings = new { DefaultConnection = connectionString }
            };
            await System.IO.File.WriteAllTextAsync(setupPath,
                JsonSerializer.Serialize(finalJson, new JsonSerializerOptions { WriteIndented = true }));

            // 5. Trigger app shutdown — host auto-restarts
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                _lifetime.StopApplication();
            });

            return Ok(new { success = true, message = "Adopted existing database. Application is restarting..." });
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
