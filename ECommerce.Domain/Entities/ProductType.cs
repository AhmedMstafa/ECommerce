using ECommerce.Domain.Common;
using ECommerce.Domain.Entityies;

namespace ECommerce.Domain.Entities
{
    public class ProductType : BaseEntity
    {
        public string Name { get; private set; } = default!;

        public ICollection<Product> Products { get; private set; } = [];

        private ProductType() { }

        private ProductType(Guid id, string name)
        {
            Id = id == default ? Guid.NewGuid() : id;
            Name = name;
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public static Result<ProductType> Create(Guid id, string name)
        {
            if (id == Guid.Empty)
                return ProductTypeErrors.EmptyId;

            if (string.IsNullOrWhiteSpace(name))
                return ProductTypeErrors.EmptyName;

            if (name.Length > 100)
                return ProductTypeErrors.NameTooLong;

            return new ProductType(id, name);
        }

        public Result UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ProductTypeErrors.EmptyName;

            if (name.Length > 100)
                return ProductTypeErrors.NameTooLong;

            Name = name;
            UpdatedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }
    }
}
