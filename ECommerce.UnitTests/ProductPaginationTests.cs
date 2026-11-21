using System.Collections.Concurrent;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Common.Pagination;
using ECommerce.UseCases.Products.Caching;
using ECommerce.UseCases.Products.Commands.CreateProduct;
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

        var repository = CreateRepository(products, 5);
        var handler = new GetAllProductsQueryHandler(repository, CreateCache());
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            PageNumber = 2,
            PageSize = 2
        });

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var page = result.Value;
        page.Items.Should().HaveCount(2);
        page.PageNumber.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(3);
        page.HasPreviousPage.Should().BeTrue();
        page.HasNextPage.Should().BeTrue();
        page.Items[0].ProductBrand.Should().Be("Brand");
        page.Items[0].ProductType.Should().Be("Type");
    }

    [Fact]
    public async Task GetAllProductsQueryHandler_ShouldServeRepeatedQueriesFromCache()
    {
        var brand = ProductBrand.Create(Guid.NewGuid(), "Brand").Value!;
        var type = ProductType.Create(Guid.NewGuid(), "Type").Value!;
        var repository = CreateRepository([CreateProduct("Alpha", 10m, brand, type)], 1);
        var handler = new GetAllProductsQueryHandler(repository, CreateCache());
        var query = new GetAllProductsQuery(new ProductSpecificationParameters());

        await handler.Handle(query, CancellationToken.None);
        await handler.Handle(query, CancellationToken.None);

        await repository.Received(1)
            .GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>());
        await repository.Received(1)
            .CountAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProductHandler_ShouldInvalidateProductCacheScope()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var repository = Substitute.For<IRepository<Product>>();
        unitOfWork.Repository<Product>().Returns(repository);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var cache = Substitute.For<ICacheService>();
        var handler = new CreateProductCommandHandler(unitOfWork, cache);

        var result = await handler.Handle(
            new CreateProductCommand(
                "Name",
                "Description",
                "picture.png",
                10m,
                Guid.NewGuid(),
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await cache.Received(1)
            .RemoveByPrefixAsync(CacheScopes.Products, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ProductCacheKeys_ShouldIncludeEveryQueryParameter()
    {
        var baseline = ProductCacheKeys.List(new ProductSpecificationParameters());
        var variants = new[]
        {
            new ProductSpecificationParameters { PageNumber = 2 },
            new ProductSpecificationParameters { PageSize = 5 },
            new ProductSpecificationParameters { Search = "laptop" },
            new ProductSpecificationParameters { BrandId = Guid.NewGuid() },
            new ProductSpecificationParameters { TypeId = Guid.NewGuid() },
            new ProductSpecificationParameters { MinPrice = 10m },
            new ProductSpecificationParameters { MaxPrice = 100m },
            new ProductSpecificationParameters { SortBy = "price" },
            new ProductSpecificationParameters { SortOrder = "desc" }
        };

        foreach (var variant in variants)
        {
            ProductCacheKeys.List(variant).Should().NotBe(baseline);
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public void QueryValidator_ShouldRejectInvalidPagination(int pageNumber, int pageSize)
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            PageNumber = pageNumber,
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
    [InlineData("name")]
    [InlineData(null)]
    public void QueryValidator_ShouldAcceptSupportedSort(string? sort)
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters { Sort = sort });

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("price")]
    [InlineData("createdat")]
    public void QueryValidator_ShouldAcceptWhitelistedSortBy(string sortBy)
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            SortBy = sortBy,
            SortOrder = "desc"
        });

        validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void QueryValidator_ShouldRejectUnsupportedSortAndLongSearch()
    {
        var validator = new GetAllProductsQueryValidator();
        var query = new GetAllProductsQuery(new ProductSpecificationParameters
        {
            Sort = "price asc; drop table",
            SortBy = "passwordHash",
            SortOrder = "sideways",
            Search = new string('x', ProductSpecificationParameters.MaxSearchLength + 1)
        });

        var validation = validator.Validate(query);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error => error.PropertyName.Contains("Sort"));
        validation.Errors.Should().Contain(error => error.PropertyName.Contains("SortBy"));
        validation.Errors.Should().Contain(error => error.PropertyName.Contains("SortOrder"));
        validation.Errors.Should().Contain(error => error.PropertyName.Contains("Search"));
    }

    [Fact]
    public void QueryValidator_ShouldRejectInvalidPriceRange()
    {
        var validator = new GetAllProductsQueryValidator();

        validator.Validate(new GetAllProductsQuery(new ProductSpecificationParameters
        {
            MinPrice = -1m
        })).IsValid.Should().BeFalse();

        validator.Validate(new GetAllProductsQuery(new ProductSpecificationParameters
        {
            MinPrice = 100m,
            MaxPrice = 10m
        })).IsValid.Should().BeFalse();

        validator.Validate(new GetAllProductsQuery(new ProductSpecificationParameters
        {
            MinPrice = 10m,
            MaxPrice = 100m
        })).IsValid.Should().BeTrue();
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
        empty.HasNextPage.Should().BeFalse();
        empty.HasPreviousPage.Should().BeFalse();

        var lastPage = new PagedResponse<string>(["a"], 3, 2, 5);
        lastPage.TotalPages.Should().Be(3);
        lastPage.HasNextPage.Should().BeFalse();
        lastPage.HasPreviousPage.Should().BeTrue();
    }

    private static ICacheService CreateCache() => new TestCacheService();

    private static IReadRepository<Product> CreateRepository(
        IReadOnlyList<Product> products,
        int totalCount)
    {
        var repository = Substitute.For<IReadRepository<Product>>();
        repository.GetAllWithSpecAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(products));
        repository.CountAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(totalCount);

        return repository;
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

    /// <summary>
    /// Small in-memory ICacheService for handler tests. Caching is an
    /// implementation detail of the infrastructure layer, so these tests only
    /// need deterministic hit/miss behaviour, not Redis.
    /// </summary>
    private sealed class TestCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, object?> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_entries.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken ct = default)
        {
            _entries[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _entries.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefixKey, CancellationToken ct = default)
        {
            foreach (var key in _entries.Keys
                         .Where(k => k.StartsWith(prefixKey, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _entries.TryRemove(key, out _);
            }

            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken ct = default)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                return (T)cached!;
            }

            var value = await factory(ct);
            _entries[key] = value;
            return value;
        }
    }
}
