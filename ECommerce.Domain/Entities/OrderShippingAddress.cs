namespace ECommerce.Domain.Entities;

public sealed class OrderShippingAddress
{
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Street { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string State { get; private set; } = default!;
    public string ZipCode { get; private set; } = default!;
    public string Country { get; private set; } = default!;

    private OrderShippingAddress() { }

    public OrderShippingAddress(
        string firstName,
        string lastName,
        string street,
        string city,
        string state,
        string zipCode,
        string country)
    {
        FirstName = firstName;
        LastName = lastName;
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    public static OrderShippingAddress FromAddress(Address address) => new(
        address.FirstName,
        address.LastName,
        address.Street,
        address.City,
        address.State,
        address.ZipCode,
        address.Country);
}
