using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities;

public sealed class Address : BaseEntity
{
    public Guid UserId { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string Street { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string State { get; private set; } = default!;
    public string ZipCode { get; private set; } = default!;
    public string Country { get; private set; } = default!;
    public bool IsDefault { get; private set; }

    private Address() { }

    private Address(
        Guid id,
        Guid userId,
        string firstName,
        string lastName,
        string street,
        string city,
        string state,
        string zipCode,
        string country,
        bool isDefault,
        DateTime utcNow)
    {
        Id = id == default ? Guid.NewGuid() : id;
        UserId = userId;
        FirstName = firstName;
        LastName = lastName;
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
        IsDefault = isDefault;
        CreatedAt = utcNow;
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public static Result<Address> Create(
        Guid userId,
        string firstName,
        string lastName,
        string street,
        string city,
        string state,
        string zipCode,
        string country,
        bool isDefault,
        DateTime utcNow,
        Guid id = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<Address>(Error.Validation("Address.InvalidUser", "A valid user is required."));

        var validation = Validate(firstName, lastName, street, city, state, zipCode, country);
        if (validation.IsFailure)
            return Result.Failure<Address>(validation.Error!);

        return new Address(id, userId, firstName.Trim(), lastName.Trim(), street.Trim(), city.Trim(), state.Trim(), zipCode.Trim(), country.Trim(), isDefault, utcNow);
    }

    public Result Update(
        string firstName,
        string lastName,
        string street,
        string city,
        string state,
        string zipCode,
        string country,
        DateTime utcNow)
    {
        var validation = Validate(firstName, lastName, street, city, state, zipCode, country);
        if (validation.IsFailure)
            return validation;

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Street = street.Trim();
        City = city.Trim();
        State = state.Trim();
        ZipCode = zipCode.Trim();
        Country = country.Trim();
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public void SetDefault(bool isDefault, DateTime utcNow)
    {
        IsDefault = isDefault;
        UpdatedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    private static Result Validate(string firstName, string lastName, string street, string city, string state, string zipCode, string country)
    {
        if (string.IsNullOrWhiteSpace(firstName)) return Result.Failure(Error.Validation("Address.FirstNameRequired", "First name is required."));
        if (string.IsNullOrWhiteSpace(lastName)) return Result.Failure(Error.Validation("Address.LastNameRequired", "Last name is required."));
        if (string.IsNullOrWhiteSpace(street)) return Result.Failure(Error.Validation("Address.StreetRequired", "Street is required."));
        if (string.IsNullOrWhiteSpace(city)) return Result.Failure(Error.Validation("Address.CityRequired", "City is required."));
        if (string.IsNullOrWhiteSpace(state)) return Result.Failure(Error.Validation("Address.StateRequired", "State is required."));
        if (string.IsNullOrWhiteSpace(zipCode)) return Result.Failure(Error.Validation("Address.ZipCodeRequired", "Zip code is required."));
        if (string.IsNullOrWhiteSpace(country)) return Result.Failure(Error.Validation("Address.CountryRequired", "Country is required."));
        return Result.Success();
    }
}
