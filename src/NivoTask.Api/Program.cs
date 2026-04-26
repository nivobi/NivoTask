using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Data;
using NivoTask.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// EF Core + Pomelo MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

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

// D-15: Authorize-by-default
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

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

builder.Services.AddOpenApi();

var app = builder.Build();

// Seed default user (D-05, D-06)
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// D-03: Serve Blazor WASM static files
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Identity API endpoints (login, register, manage/info) -- AllowAnonymous per D-15
app.MapIdentityApi<AppUser>().AllowAnonymous();

// Custom logout endpoint (CSRF-safe: requires POST with body + auth)
app.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return TypedResults.Ok();
}).RequireAuthorization();

// Simple user info endpoint protected by default fallback policy
app.MapGet("/api/me", (HttpContext context) =>
{
    var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? context.User.Identity?.Name;
    return TypedResults.Ok(new { email });
});

app.MapFallbackToFile("index.html");

app.Run();

// Make Program class accessible to integration tests
public partial class Program { }
