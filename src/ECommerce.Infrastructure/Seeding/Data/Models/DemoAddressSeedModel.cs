namespace ECommerce.Infrastructure.Seeding.Data.Models;

public sealed record DemoAddressSeedModel(
    Guid Id,
    string FirstName,
    string LastName,
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country,
    bool IsDefault);
