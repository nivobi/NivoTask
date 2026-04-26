using Microsoft.AspNetCore.Identity;
using NivoTask.Api.Models;

namespace NivoTask.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration config)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<AppUser>>();

        var seedSection = config.GetSection("SeedUser");
        var email = seedSection["Email"] ?? "admin@nivotask.local";
        var password = seedSection["Password"] ?? "Change_Me_123";

        if (await userManager.FindByEmailAsync(email) is null)
        {
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
}
