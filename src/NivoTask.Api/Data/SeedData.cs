using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NivoTask.Api.Models;

namespace NivoTask.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration config)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<AppUser>>();

        // Skip seeding if any users exist (wizard already created admin)
        if (await userManager.Users.AnyAsync())
            return;

        // Backward compat: seed from config for pre-wizard installs
        var seedSection = config.GetSection("SeedUser");
        var email = seedSection["Email"];
        var password = seedSection["Password"];

        // No SeedUser config and no users = wizard should handle this
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            return;

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }
}
