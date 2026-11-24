using ECommerce.Domain.Entities;
using ECommerce.UseCases.Brands.Dtos;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Types.Dtos;
using Mapster;

namespace ECommerce.UseCases;

public class MappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, GetAllProductsResponse>()
            .Map(dest => dest.ProductBrand, src => src.Brand.Name)
            .Map(dest => dest.ProductType, src => src.Type.Name);

        config.NewConfig<Product, GetByIdProductResponse>()
            .Map(dest => dest.ProductBrand, src => src.Brand.Name)
            .Map(dest => dest.ProductType, src => src.Type.Name);

        config.NewConfig<ProductBrand, GetAllBrandsResponse>();

        config.NewConfig<ProductType, GetAllTypesResponse>();
    }
}
