using ECommerce.Domain.Entityies;

namespace ECommerce.Domain.Entities
{
    public class Product : BaseEntity
    {
        public string Name { get; protected set; } = default!;

        public string Description { get; protected set; } = default!;

        public string PictureUrl { get; protected set; } = default!;

        public decimal Price { get; protected set; } = default!;

        public Guid ProductBrandId { get; set; }

        public ProductBrand Brand { get; protected set; } = default!;

        public Guid ProductTypeId { get; protected set; } = default!;
        

        public ProductType Type { get; protected set; } = default!;




    }
}
