namespace ECommerce.UseCases.Orders.Dtos;

public sealed record OrderShippingAddressResponse(
    string FirstName,
    string LastName,
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country);
