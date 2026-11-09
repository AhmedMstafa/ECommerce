namespace ECommerce.Domain.Common
{
    public static class ProductTypeErrors
    {
        public static readonly Error EmptyId =
            Error.Validation("ProductType.EmptyId", "Product type ID is required.");

        public static readonly Error EmptyName =
            Error.Validation("ProductType.EmptyName", "Product type name cannot be empty.");

        public static readonly Error NameTooLong =
            Error.Validation("ProductType.NameTooLong", "Product type name cannot exceed 100 characters.");

        public static Error NotFound(Guid id) =>
            Error.NotFound("ProductType.NotFound", $"Product type with id '{id}' was not found.");
    }
}
