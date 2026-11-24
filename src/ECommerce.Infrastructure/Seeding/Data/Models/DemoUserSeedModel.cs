namespace ECommerce.Infrastructure.Seeding.Data.Models;

public sealed record DemoUserSeedModel(
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<DemoAddressSeedModel> Addresses);
