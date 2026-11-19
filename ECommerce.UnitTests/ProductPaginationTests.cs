using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases;
using ECommerce.UseCases.Common.Pagination;
using ECommerce.UseCases.Products.Queries;
using ECommerce.UseCases.Products.Specifications;
using FluentAssertions;
using Mapster;
using NSubstitute;

namespace ECommerce.UnitTests;

public class ProductPaginationTests
{
    public ProductPaginationTests()
    {
        TypeAdapterConfig.GlobalSettings.Scan(typeof(MappingConfig).Assembly);
    }

    [Fact]
    public async Task GetAllProductsQueryHandler_ShouldReturnPageWithTotalCount()
    {
        var brand = ProductBrand.Create(Guid.NewGuid(), "Brand").Value!;
        var type = ProductType.Create(Guid.NewGuid(), "Type").Value!;
        var products = new[]
        {
            CreateProduct("Beta", 20m, brand, type),
            CreateProduct("Alpha", 10m, brand, type)
        };

        var repository = Substitute.For<IReadRepository<Product>>();
        repository.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Product>>(products));
        repository.CountAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(5);

        var handler = new GetAllProductsQueryHandler(repository);
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            PageIndex = 2,
            PageSize = 2
        });

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var page = result.Value;
        page.Items.Should().HaveCount(2);
        page.PageIndex.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(3);
        page.HasPrevious.Should().BeTrue();
        page.HasNext.Should().BeTrue();
        page.Items[0].ProductBrand.Should().Be("Brand");
        page.Items[0].ProductType.Should().Be("Type");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public void QueryValidator_ShouldRejectInvalidPagination(int pageIndex, int pageSize)
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            PageIndex = pageIndex,
            PageSize = pageSize
        });

        var validation = validator.Validate(query);

        validation.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("priceAsc")]
    [InlineData("priceDesc")]
    [InlineData("nameAsc")]
    [InlineData("nameDesc")]
    [InlineData(null)]
    public void QueryValidator_ShouldAcceptSupportedSort(string? sort)
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters { Sort = sort });

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void QueryValidator_ShouldRejectUnsupportedSortAndLongSearch()
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            Sort = "price asc; drop table",
            Search = new string('x', ProductSpecificationParameters.MaxSearchLength + 1)
        });

        var validation = validator.Validate(query);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error => error.PropertyName.Contains("Sort"));
        validation.Errors.Should().Contain(error => error.PropertyName.Contains("Search"));
    }

    [Fact]
    public void GetProductByIdValidator_ShouldRejectEmptyId()
    {
        var validator = new GetProductByIdQueryValidator();

        validator.Validate(new GetProductByIdQuery(Guid.Empty)).IsValid.Should().BeFalse();
        validator.Validate(new GetProductByIdQuery(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PagedResponse_ShouldReportCorrectBoundaries()
    {
        var empty = new PagedResponse<string>([], 1, 10, 0);
        empty.TotalPages.Should().Be(0);
        empty.HasNext.Should().BeFalse();
        empty.HasPrevious.Should().BeFalse();

        var lastPage = new PagedResponse<string>(["a"], 3, 2, 5);
        lastPage.TotalPages.Should().Be(3);
        lastPage.HasNext.Should().BeFalse();
        lastPage.HasPrevious.Should().BeTrue();
    }

    private static Product CreateProduct(string name, decimal price, ProductBrand brand, ProductType type)
    {
        var product = Product.Create(
            name,
            "Description",
            "picture.png",
            price,
            brand.Id,
            type.Id).Value!;

        SetNavigation(product, "Brand", brand);
        SetNavigation(product, "Type", type);
        return product;
    }

    private static void SetNavigation(object target, string propertyName, object value)
    {
        var setter = target.GetType().GetProperty(propertyName)!.GetSetMethod(nonPublic: true)!;
        setter.Invoke(target, [value]);
    }
}
