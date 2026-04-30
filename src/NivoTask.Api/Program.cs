using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Load setup.json overlay (contains connection string after wizard completes)
builder.Configuration.AddJsonFile("setup.json", optional: true, reloadOnChange: true);

var setupComplete = builder.Configuration.GetValue<bool>("SetupComplete");
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (setupComplete)
{
    // ── Normal mode: full app with EF Core + Identity ──

    // EF Core + Pomelo MySQL (skip if no connection string — tests override via DI)
    if (!string.IsNullOrEmpty(connectionString))
    {
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
    }

    // Persist Data Protection keys to disk so cookies survive app pool recycles.
    // Without this, IIS OutOfProcess regenerates keys in-memory on every restart
    // and existing auth cookies become undecryptable -> users get logged out.
    var keysPath = Path.Combine(builder.Environment.ContentRootPath, "dp-keys");
    Directory.CreateDirectory(keysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("NivoTask");

    // Identity with cookie auth
    builder.Services
        .AddAuthentication(IdentityConstants.ApplicationScheme)
        .AddIdentityCookies();

    builder.Services.AddIdentityCore<AppUser>(options =>
        {
            // D-13: Relaxed password policy
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;

            // D-14: Lockout policy
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;

            // Single-user app -- no email confirmation flow needed
            options.SignIn.RequireConfirmedEmail = false;
            options.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddApiEndpoints();

    builder.Services.AddAuthorization();

    // D-12: Persistent cookie expiration (30 days)
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // Return 401 instead of redirect for API calls (Pitfall 2 from RESEARCH.md)
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Update service: GitHub release polling + self-replace flow
builder.Services.AddHttpClient("github", c =>
{
    c.BaseAddress = new Uri("https://api.github.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("NivoTask-Updater/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    c.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddSingleton<NivoTask.Api.Services.UpdateService>();

// Background sweep that closes timers running >24h (browser crash / app close orphans).
if (setupComplete)
{
    builder.Services.AddHostedService<NivoTask.Api.Services.StaleTimerCleanupService>();
}

// Health check (mapped at /healthz). Always registered; DB check only when set up.
var healthChecks = builder.Services.AddHealthChecks();
if (setupComplete)
{
    healthChecks.AddCheck<NivoTask.Api.Services.DbHealthCheck>("database");
}

var app = builder.Build();

if (setupComplete)
{
    // Run migrations + seed (normal mode only)
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.ProviderName == "Pomelo.EntityFrameworkCore.MySql")
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            // Non-MySQL provider (SQLite/InMemory for integration tests) -- create schema without migrations
            await db.Database.EnsureCreatedAsync();
        }
        await SeedData.InitializeAsync(scope.ServiceProvider, builder.Configuration);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// D-03: Serve Blazor WASM static files (always — wizard UI needs them too)
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

if (setupComplete)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapControllers();

// /healthz — anonymous, returns JSON { status, version, checks }
app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var update = ctx.RequestServices.GetRequiredService<NivoTask.Api.Services.UpdateService>();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            version = update.GetCurrentVersion(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        });
        await ctx.Response.WriteAsync(payload);
    }
}).AllowAnonymous();

if (setupComplete)
{
    // Identity API endpoints (login, register, manage/info) -- AllowAnonymous per D-15
    app.MapIdentityApi<AppUser>().AllowAnonymous();

    // Custom logout endpoint (CSRF-safe: requires POST with body + auth)
    app.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return TypedResults.Ok();
    }).RequireAuthorization();

    // User info endpoint — explicitly protected (no fallback policy)
    app.MapGet("/api/me", (HttpContext context) =>
    {
        var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? context.User.Identity?.Name;
        return TypedResults.Ok(new { email });
    }).RequireAuthorization();
}

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

// Make Program class accessible to integration tests
public partial class Program { }
