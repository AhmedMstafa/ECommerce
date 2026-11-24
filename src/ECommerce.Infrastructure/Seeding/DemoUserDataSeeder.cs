using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Seeding.Data;
using ECommerce.Infrastructure.Seeding.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Seeding;

/// <summary>
/// Creates demo customers and their addresses in Development. Passwords are
/// never stored in source: all demo users share SeedDemoUsers:Password.
/// Existing users are not modified; addresses are added only when their fixed
/// id is missing.
/// </summary>
public sealed class DemoUserDataSeeder(
    StoreDbContext dbContext,
    UserManager<AppUser> userManager,
    IConfiguration configuration,
    ILogger<DemoUserDataSeeder> logger) : IDataSeeder
{
    public int Order => 5;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (bool.TryParse(configuration["SeedDemoUsers:Enabled"], out var enabled) && !enabled)
        {
            return;
        }

        var models = await JsonSeeder.ReadAsync<DemoUserSeedModel>("demo-users.json", cancellationToken);

        if (models.Count == 0)
        {
            return;
        }

        var password = configuration["SeedDemoUsers:Password"];
        var existingAddressIds = (await dbContext.Addresses
                .IgnoreQueryFilters()
                .Select(x => x.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var usersWithDefaultAddress = (await dbContext.Addresses
                .Where(x => x.IsDefault)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var utcNow = DateTime.UtcNow;

        foreach (var model in models)
        {
            var email = model.Email.Trim().ToLowerInvariant();
            var user = await userManager.FindByEmailAsync(email);

            if (user is not null && user.IsDeleted)
            {
                logger.LogWarning("Demo user {Email} was skipped because the account is deleted.", email);
                continue;
            }

            if (user is null)
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    logger.LogWarning(
                        "Demo user {Email} was skipped because SeedDemoUsers:Password is not configured.",
                        email);
                    continue;
                }

                user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    UserName = email,
                    EmailConfirmed = true,
                    FirstName = model.FirstName,
                    LastName = model.LastName
                };

                var createResult = await userManager.CreateAsync(user, password);

                if (!createResult.Succeeded)
                {
                    logger.LogWarning(
                        "Demo user {Email} was skipped: {Errors}",
                        email,
                        string.Join(", ", createResult.Errors.Select(x => x.Description)));
                    continue;
                }
            }

            foreach (var address in model.Addresses)
            {
                if (existingAddressIds.Contains(address.Id))
                {
                    continue;
                }

                var isDefault = address.IsDefault && !usersWithDefaultAddress.Contains(user.Id);
                var addressResult = Address.Create(
                    user.Id,
                    address.FirstName,
                    address.LastName,
                    address.Street,
                    address.City,
                    address.State,
                    address.ZipCode,
                    address.Country,
                    isDefault,
                    utcNow,
                    address.Id);

                if (addressResult.IsFailure)
                {
                    logger.LogWarning(
                        "Address {AddressId} for {Email} was skipped: {Error}",
                        address.Id,
                        email,
                        addressResult.Error?.Message);
                    continue;
                }

                dbContext.Addresses.Add(addressResult.Value);
                existingAddressIds.Add(address.Id);

                if (isDefault)
                {
                    usersWithDefaultAddress.Add(user.Id);
                }
            }
        }
    }
}
