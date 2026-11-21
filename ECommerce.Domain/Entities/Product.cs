using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities
{
    public class Product : BaseEntity
    {
        public string Name { get; private set; } = default!;
        public string Description { get; private set; } = default!;
        public string PictureUrl { get; private set; } = default!;
        public decimal Price { get; private set; } = default!;

        /// <summary>
        /// Current sellable quantity. Baskets read this from SQL as the
        /// authoritative availability value; adding to a basket never reserves it.
        /// </summary>
        public int StockQuantity { get; private set; }

        public Guid ProductBrandId { get; private set; }
        public ProductBrand Brand { get; private set; } = default!;

        public Guid ProductTypeId { get; private set; }
        public ProductType Type { get; private set; } = default!;

        private Product() { }

        private Product(
            string name,
            string description,
            string pictureUrl,
            decimal price,
            Guid productBrandId,
            Guid productTypeId,
            Guid id = default,
            int stockQuantity = 0)
        {
            Id = id == default ? Guid.NewGuid() : id;
            Name = name;
            Description = description;
            PictureUrl = pictureUrl;
            Price = price;
            StockQuantity = stockQuantity;
            ProductBrandId = productBrandId;
            ProductTypeId = productTypeId;
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public static Result<Product> Create(
            string name,
            string description,
            string pictureUrl,
            decimal price,
            Guid productBrandId,
            Guid productTypeId,
            Guid id = default,
            int stockQuantity = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ProductErrors.EmptyName;

            if (name.Length > 200)
                return ProductErrors.NameTooLong;

            if (string.IsNullOrWhiteSpace(description))
                return ProductErrors.EmptyDescription;

            if (description.Length > 2000)
                return ProductErrors.DescriptionTooLong;

            if (string.IsNullOrWhiteSpace(pictureUrl))
                return ProductErrors.EmptyPictureUrl;

            if (pictureUrl.Length > 500)
                return ProductErrors.PictureUrlTooLong;

            if (price < 0)
                return ProductErrors.NegativePrice;

            if (stockQuantity < 0)
                return ProductErrors.NegativeStock;

            if (productBrandId == Guid.Empty)
                return ProductErrors.EmptyBrand;

            if (productTypeId == Guid.Empty)
                return ProductErrors.EmptyType;

            return new Product(name, description, pictureUrl, price, productBrandId, productTypeId, id, stockQuantity);
        }

        public Result UpdateDetails(
            string name,
            string description,
            string pictureUrl,
            Guid productBrandId,
            Guid productTypeId)
        {
            var nameResult = UpdateName(name);
            if (nameResult.IsFailure)
                return nameResult;

            var descResult = UpdateDescription(description);
            if (descResult.IsFailure)
                return descResult;

            var picResult = UpdatePictureUrl(pictureUrl);
            if (picResult.IsFailure)
                return picResult;

            var brandResult = UpdateBrand(productBrandId);
            if (brandResult.IsFailure)
                return brandResult;

            var typeResult = UpdateType(productTypeId);
            if (typeResult.IsFailure)
                return typeResult;

            return Result.Success();
        }

        public Result UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ProductErrors.EmptyName;

            if (name.Length > 200)
                return ProductErrors.NameTooLong;

            Name = name;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        public Result UpdateDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return ProductErrors.EmptyDescription;

            if (description.Length > 2000)
                return ProductErrors.DescriptionTooLong;

            Description = description;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        public Result UpdatePictureUrl(string pictureUrl)
        {
            if (string.IsNullOrWhiteSpace(pictureUrl))
                return ProductErrors.EmptyPictureUrl;

            if (pictureUrl.Length > 500)
                return ProductErrors.PictureUrlTooLong;

            PictureUrl = pictureUrl;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        public Result UpdatePrice(decimal newPrice)
        {
            if (newPrice < 0)
                return ProductErrors.NegativePrice;

            Price = newPrice;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        public Result UpdateStock(int stockQuantity)
        {
            if (stockQuantity < 0)
                return ProductErrors.NegativeStock;

            StockQuantity = stockQuantity;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        public Result UpdateBrand(Guid productBrandId)
        {
            if (productBrandId == Guid.Empty)
                return ProductErrors.EmptyBrand;

            ProductBrandId = productBrandId;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }

        public Result UpdateType(Guid productTypeId)
        {
            if (productTypeId == Guid.Empty)
                return ProductErrors.EmptyType;

            ProductTypeId = productTypeId;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }
    }
}
