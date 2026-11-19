using ECommerce.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Seeding;

/// <summary>
/// Creates the application roles. An administrator account is created only when
/// SeedAdmin:Email and SeedAdmin:Password are supplied through configuration
/// (environment variables / user secrets), never from source control.
/// </summary>
public sealed class IdentitySeeder(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<AppUser> userManager,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger) : IDataSeeder
{
    public int Order => 0;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));

            if (!roleResult.Succeeded)
            {
                logger.LogError("Failed to create {Role} role: {Errors}",
                    AppRoles.Admin,
                    string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                return;
            }
        }

        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Optional: roles exist, an operator can assign Admin to any account.
            return;
        }

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                FirstName = "System",
                LastName = "Administrator"
            };

            var createResult = await userManager.CreateAsync(user, password);

            if (!createResult.Succeeded)
            {
                logger.LogError("Failed to seed admin user: {Errors}",
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return;
            }
        }

        if (!await userManager.IsInRoleAsync(user, AppRoles.Admin))
        {
            var addResult = await userManager.AddToRoleAsync(user, AppRoles.Admin);

            if (!addResult.Succeeded)
            {
                logger.LogError("Failed to assign {Role} role: {Errors}",
                    AppRoles.Admin,
                    string.Join(", ", addResult.Errors.Select(e => e.Description)));
            }
        }
    }
}
