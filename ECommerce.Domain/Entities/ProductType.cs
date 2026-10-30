using ECommerce.Domain.Entityies;

namespace ECommerce.Domain.Entities
{
    public class ProductType : BaseEntity
    {
        public string Name { get; protected set; } = default!;

        public ICollection<Product> Products { get; protected set; } = [];


    }
}
