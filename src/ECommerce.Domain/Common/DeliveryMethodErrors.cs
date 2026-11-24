namespace ECommerce.Domain.Common;

public static class DeliveryMethodErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("DeliveryMethod.NotFound", $"Delivery method with id '{id}' was not found.");

    public static readonly Error NameRequired =
        Error.Validation("DeliveryMethod.NameRequired", "Delivery method name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("DeliveryMethod.NameTooLong", "Delivery method name cannot exceed 200 characters.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("DeliveryMethod.DescriptionTooLong", "Delivery method description cannot exceed 1000 characters.");

    public static readonly Error InvalidPrice =
        Error.Validation("DeliveryMethod.InvalidPrice", "Delivery method price cannot be negative.");

    public static readonly Error AlreadyInactive =
        Error.Conflict("DeliveryMethod.AlreadyInactive", "The delivery method is already inactive.");
}
