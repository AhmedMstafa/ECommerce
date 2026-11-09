using ECommerce.Domain.Common;
using ECommerce.Domain.Entityies;

namespace ECommerce.Domain.Entities
{
    public class ProductBrand : BaseEntity
    {
        public string Name { get; private set; } = default!;

        public ICollection<Product> Products { get; private set; } = [];

        private ProductBrand() { }

        private ProductBrand(Guid id, string name)
        {
            Id = id == default ? Guid.NewGuid() : id;
            Name = name;
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public static Result<ProductBrand> Create(Guid id, string name)
        {
            if (id == Guid.Empty)
                return ProductBrandErrors.EmptyId;

            if (string.IsNullOrWhiteSpace(name))
                return ProductBrandErrors.EmptyName;

            if (name.Length > 100)
                return ProductBrandErrors.NameTooLong;

            return new ProductBrand(id, name);
        }

        public Result UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ProductBrandErrors.EmptyName;

            if (name.Length > 100)
                return ProductBrandErrors.NameTooLong;

            Name = name;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }
    }
}
