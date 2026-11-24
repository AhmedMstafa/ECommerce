namespace ECommerce.UseCases.Users.Dtos;

public sealed record AddressResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country,
    bool IsDefault);