using ECommerce.Domain.Entities;
using ECommerce.Domain.Specifications;
using ECommerce.Domain.Specifications.Builders;
using ECommerce.Infrastructure.Specifications;
using ECommerce.UseCases.Products.Specifications;
using FluentAssertions;
using Xunit;

namespace ECommerce.ArchitectureTests;

public class SpecificationTests
{
    private readonly List<Product> _products;
    private readonly Guid _brand1 = Guid.NewGuid();
    private readonly Guid _brand2 = Guid.NewGuid();
    private readonly Guid _type1 = Guid.NewGuid();
    private readonly Guid _type2 = Guid.NewGuid();

    public SpecificationTests()
    {
        _products =
        [
            Product.Create("Alpha Phone", "Description A", "pic1.png", 500m, _brand1, _type1).Value,
            Product.Create("Beta Laptop", "Description B", "pic2.png", 1500m, _brand1, _type2).Value,
            Product.Create("Gamma Watch", "Description C", "pic3.png", 300m, _brand2, _type1).Value,
            Product.Create("Delta Tablet", "Description D", "pic4.png", 700m, _brand2, _type2).Value
        ];
    }

    [Fact]
    public void Specification_ShouldFilterByBrandAndType()
    {
        // Arrange
        var parameters = new ProductSpecificationParameters
        {
            BrandId = _brand1,
            TypeId = _type1
        };
        var spec = new ProductsWithBrandAndTypeSpecification(parameters);

        // Act
        var query = SpecificationEvaluator.GetQuery(_products.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Alpha Phone");
    }

    [Fact]
    public void Specification_ShouldFilterBySearch()
    {
        // Arrange
        var parameters = new ProductSpecificationParameters
        {
            Search = "laptop"
        };
        var spec = new ProductsWithBrandAndTypeSpecification(parameters);

        // Act
        var query = SpecificationEvaluator.GetQuery(_products.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Beta Laptop");
    }

    [Fact]
    public void Specification_ShouldSortByPriceAscending()
    {
        // Arrange
        var parameters = new ProductSpecificationParameters
        {
            Sort = "priceAsc",
            PageSize = 10
        };
        var spec = new ProductsWithBrandAndTypeSpecification(parameters);

        // Act
        var query = SpecificationEvaluator.GetQuery(_products.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result.Should().HaveCount(4);
        result.Select(p => p.Price).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Specification_ShouldSortByPriceDescending()
    {
        // Arrange
        var parameters = new ProductSpecificationParameters
        {
            Sort = "priceDesc",
            PageSize = 10
        };
        var spec = new ProductsWithBrandAndTypeSpecification(parameters);

        // Act
        var query = SpecificationEvaluator.GetQuery(_products.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result.Should().HaveCount(4);
        result.Select(p => p.Price).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Specification_ShouldApplyPaging()
    {
        // Arrange
        var parameters = new ProductSpecificationParameters
        {
            PageIndex = 2,
            PageSize = 2,
            Sort = "priceAsc"
        };
        var spec = new ProductsWithBrandAndTypeSpecification(parameters);

        // Act
        var query = SpecificationEvaluator.GetQuery(_products.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result.Should().HaveCount(2);
        // Page 1 has 300 (Gamma) and 500 (Alpha). Page 2 should have 700 (Delta) and 1500 (Beta).
        result[0].Name.Should().Be("Delta Tablet");
        result[1].Name.Should().Be("Beta Laptop");
    }

    [Fact]
    public void SpecificationWithProjection_ShouldSelectMappedDto()
    {
        // Arrange
        var spec = new ProductNameAndPriceSpec();

        // Act
        var query = SpecificationEvaluator.GetQuery(_products.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result.Should().HaveCount(4);
        result[0].Should().Be(("Alpha Phone", 500m));
    }

    [Fact]
    public void SpecificationBuilder_ShouldConfigureIncludeAndThenIncludeProperly()
    {
        // Arrange
        var spec = new ComplexProductSpecification();

        // Assert
        spec.IncludeExpressions.Should().HaveCount(2);

        var firstInclude = spec.IncludeExpressions[0];
        firstInclude.Type.Should().Be(IncludeType.Include);
        firstInclude.EntityType.Should().Be(typeof(Product));
        firstInclude.PropertyType.Should().Be(typeof(ProductBrand));
        firstInclude.PreviousPropertyType.Should().BeNull();

        var thenInclude = spec.IncludeExpressions[1];
        thenInclude.Type.Should().Be(IncludeType.ThenInclude);
        thenInclude.EntityType.Should().Be(typeof(Product));
        thenInclude.PreviousPropertyType.Should().Be(typeof(ProductBrand));
    }

    [Fact]
    public void SpecificationBuilder_ShouldSupportFluentPagingAndSorting()
    {
        // Arrange
        var spec = new FluentTestSpecification();

        // Assert
        spec.IsPagingEnabled.Should().BeTrue();
        spec.Skip.Should().Be(10);
        spec.Take.Should().Be(5);
        spec.OrderExpressions.Should().HaveCount(2);
        spec.OrderExpressions[0].OrderType.Should().Be(OrderType.OrderBy);
        spec.OrderExpressions[1].OrderType.Should().Be(OrderType.ThenByDescending);
    }

    [Fact]
    public void GetAllBrandsSpecification_ShouldOrderByName()
    {
        // Arrange
        var brands = new List<ProductBrand>
        {
            ProductBrand.Create(Guid.NewGuid(), "Puma").Value,
            ProductBrand.Create(Guid.NewGuid(), "Adidas").Value,
            ProductBrand.Create(Guid.NewGuid(), "Nike").Value
        };
        var spec = new UseCases.Brands.Specifications.GetAllBrandsSpecification();

        // Act
        var query = SpecificationEvaluator.GetQuery(brands.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result[0].Name.Should().Be("Adidas");
        result[1].Name.Should().Be("Nike");
        result[2].Name.Should().Be("Puma");
    }

    [Fact]
    public void GetAllTypesSpecification_ShouldOrderByName()
    {
        // Arrange
        var types = new List<ProductType>
        {
            ProductType.Create(Guid.NewGuid(), "Shoes").Value,
            ProductType.Create(Guid.NewGuid(), "Accessories").Value,
            ProductType.Create(Guid.NewGuid(), "Boots").Value
        };
        var spec = new UseCases.Types.Specifications.GetAllTypesSpecification();

        // Act
        var query = SpecificationEvaluator.GetQuery(types.AsQueryable(), spec);
        var result = query.ToList();

        // Assert
        result[0].Name.Should().Be("Accessories");
        result[1].Name.Should().Be("Boots");
        result[2].Name.Should().Be("Shoes");
    }

    private sealed class FluentTestSpecification : BaseSpecification<Product>
    {
        public FluentTestSpecification()
        {
            Query.Where(p => p.Price > 100)
                 .OrderBy(p => p.Price)
                 .ThenByDescending(p => p.Name)
                 .Paginate(pageIndex: 3, pageSize: 5)
                 .AsNoTracking();
        }
    }

    private sealed class ComplexProductSpecification : BaseSpecification<Product>
    {
        public ComplexProductSpecification()
        {
            Query.Include(p => p.Brand)
                 .ThenInclude(b => b.Name);
        }
    }

    private sealed class ProductNameAndPriceSpec : BaseSpecification<Product, (string Name, decimal Price)>
    {
        public ProductNameAndPriceSpec()
        {
            AddSelector(p => new ValueTuple<string, decimal>(p.Name, p.Price));
        }
    }
}
