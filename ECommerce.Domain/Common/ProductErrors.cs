namespace ECommerce.Domain.Common
{
    public static class ProductErrors
    {
        public static readonly Error EmptyName =
            Error.Validation("Product.EmptyName", "Product name cannot be empty.");

        public static readonly Error NameTooLong =
            Error.Validation("Product.NameTooLong", "Product name cannot exceed 200 characters.");

        public static readonly Error EmptyDescription =
            Error.Validation("Product.EmptyDescription", "Product description cannot be empty.");

        public static readonly Error DescriptionTooLong =
            Error.Validation("Product.DescriptionTooLong", "Product description cannot exceed 2000 characters.");

        public static readonly Error EmptyPictureUrl =
            Error.Validation("Product.EmptyPictureUrl", "Product picture URL cannot be empty.");

        public static readonly Error PictureUrlTooLong =
            Error.Validation("Product.PictureUrlTooLong", "Product picture URL cannot exceed 500 characters.");

        public static readonly Error NegativePrice =
            Error.Validation("Product.NegativePrice", "Product price cannot be negative.");

        public static readonly Error NegativeStock =
            Error.Validation("Product.NegativeStock", "Product stock quantity cannot be negative.");

        public static readonly Error EmptyBrand =
            Error.Validation("Product.EmptyBrand", "Product brand is required.");

        public static readonly Error EmptyType =
            Error.Validation("Product.EmptyType", "Product type is required.");

        public static Error NotFound(Guid id) =>
            Error.NotFound("Product.NotFound", $"Product with id '{id}' was not found.");
    }
}
