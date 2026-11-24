namespace ECommerce.Domain.Common
{
    public static class ProductBrandErrors
    {
        public static readonly Error EmptyId =
            Error.Validation("ProductBrand.EmptyId", "Brand ID is required.");

        public static readonly Error EmptyName =
            Error.Validation("ProductBrand.EmptyName", "Brand name cannot be empty.");

        public static readonly Error NameTooLong =
            Error.Validation("ProductBrand.NameTooLong", "Brand name cannot exceed 100 characters.");

        public static Error NotFound(Guid id) =>
            Error.NotFound("ProductBrand.NotFound", $"Brand with id '{id}' was not found.");
    }
}
