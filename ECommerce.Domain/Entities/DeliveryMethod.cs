using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities;

public sealed class DeliveryMethod : AuditedEntity
{
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; }

    private DeliveryMethod() { }

    private DeliveryMethod(Guid id, string name, string? description, decimal price, DateTime utcNow)
    {
        Id = id == default ? Guid.NewGuid() : id;
        Name = name;
        Description = description;
        Price = price;
        IsActive = true;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public static Result<DeliveryMethod> Create(string name, string? description, decimal price, DateTime utcNow, Guid id = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DeliveryMethodErrors.NameRequired;
        if (name.Trim().Length > 200)
            return DeliveryMethodErrors.NameTooLong;
        if (description is not null && description.Length > 1000)
            return DeliveryMethodErrors.DescriptionTooLong;
        if (price < 0)
            return DeliveryMethodErrors.InvalidPrice;

        return new DeliveryMethod(id, name.Trim(), description?.Trim(), price, utcNow);
    }

    public Result Update(string name, string? description, decimal price, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DeliveryMethodErrors.NameRequired;
        if (name.Trim().Length > 200)
            return DeliveryMethodErrors.NameTooLong;
        if (description is not null && description.Length > 1000)
            return DeliveryMethodErrors.DescriptionTooLong;
        if (price < 0)
            return DeliveryMethodErrors.InvalidPrice;

        Name = name.Trim();
        Description = description?.Trim();
        Price = price;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }

    public Result Deactivate(DateTime utcNow)
    {
        if (!IsActive)
            return DeliveryMethodErrors.AlreadyInactive;

        IsActive = false;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
        return Result.Success();
    }
}
