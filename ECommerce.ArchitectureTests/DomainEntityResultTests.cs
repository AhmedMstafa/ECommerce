using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ECommerce.ArchitectureTests;

public class DomainEntityResultTests
{
    [Fact]
    public void Product_Create_WithValidData_ShouldReturnSuccess()
    {
        // Arrange
        var brandId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        // Act
        var result = Product.Create("Running Shoes", "High performance shoes", "https://example.com/shoes.png", 99.99m, brandId, typeId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Name.Should().Be("Running Shoes");
        result.Value.Description.Should().Be("High performance shoes");
        result.Value.PictureUrl.Should().Be("https://example.com/shoes.png");
        result.Value.Price.Should().Be(99.99m);
        result.Value.ProductBrandId.Should().Be(brandId);
        result.Value.ProductTypeId.Should().Be(typeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Product_Create_WithEmptyName_ShouldReturnEmptyNameError(string? invalidName)
    {
        // Act
        var result = Product.Create(invalidName!, "Description", "https://example.com/pic.png", 50m, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProductErrors.EmptyName);
    }

    [Fact]
    public void Product_Create_WithNameExceeding200Chars_ShouldReturnNameTooLongError()
    {
        // Arrange
        var longName = new string('a', 201);

        // Act
        var result = Product.Create(longName, "Description", "https://example.com/pic.png", 50m, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProductErrors.NameTooLong);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Product_Create_WithEmptyDescription_ShouldReturnEmptyDescriptionError(string? invalidDescription)
    {
        // Act
        var result = Product.Create("Product 1", invalidDescription!, "https://example.com/pic.png", 50m, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProductErrors.EmptyDescription);
    }

    [Fact]
    public void Product_Create_WithNegativePrice_ShouldReturnNegativePriceError()
    {
        // Act
        var result = Product.Create("Product 1", "Desc", "https://example.com/pic.png", -10m, Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProductErrors.NegativePrice);
    }

    [Fact]
    public void Product_Create_WithEmptyBrandOrType_ShouldReturnAppropriateError()
    {
        // Act
        var emptyBrandResult = Product.Create("Product 1", "Desc", "https://example.com/pic.png", 10m, Guid.Empty, Guid.NewGuid());
        var emptyTypeResult = Product.Create("Product 1", "Desc", "https://example.com/pic.png", 10m, Guid.NewGuid(), Guid.Empty);

        // Assert
        emptyBrandResult.IsFailure.Should().BeTrue();
        emptyBrandResult.Error.Should().Be(ProductErrors.EmptyBrand);

        emptyTypeResult.IsFailure.Should().BeTrue();
        emptyTypeResult.Error.Should().Be(ProductErrors.EmptyType);
    }

    [Fact]
    public void Product_UpdatePrice_WithValidPrice_ShouldUpdateSuccessfully()
    {
        // Arrange
        var product = Product.Create("Product 1", "Desc", "https://example.com/pic.png", 10m, Guid.NewGuid(), Guid.NewGuid()).Value;

        // Act
        var result = product.UpdatePrice(150m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        product.Price.Should().Be(150m);
    }

    [Fact]
    public void Product_UpdatePrice_WithNegativePrice_ShouldReturnError()
    {
        // Arrange
        var product = Product.Create("Product 1", "Desc", "https://example.com/pic.png", 10m, Guid.NewGuid(), Guid.NewGuid()).Value;

        // Act
        var result = product.UpdatePrice(-5m);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ProductErrors.NegativePrice);
        product.Price.Should().Be(10m);
    }

    [Fact]
    public void ProductBrand_Create_WithValidData_ShouldReturnSuccess()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var result = ProductBrand.Create(id, "Nike");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.Name.Should().Be("Nike");
    }

    [Fact]
    public void ProductBrand_Create_WithEmptyIdOrName_ShouldReturnError()
    {
        // Act
        var emptyIdResult = ProductBrand.Create(Guid.Empty, "Nike");
        var emptyNameResult = ProductBrand.Create(Guid.NewGuid(), "");

        // Assert
        emptyIdResult.IsFailure.Should().BeTrue();
        emptyIdResult.Error.Should().Be(ProductBrandErrors.EmptyId);

        emptyNameResult.IsFailure.Should().BeTrue();
        emptyNameResult.Error.Should().Be(ProductBrandErrors.EmptyName);
    }

    [Fact]
    public void ProductType_Create_WithValidData_ShouldReturnSuccess()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var result = ProductType.Create(id, "Shoes");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(id);
        result.Value.Name.Should().Be("Shoes");
    }

    [Fact]
    public void ProductType_Create_WithEmptyIdOrName_ShouldReturnError()
    {
        // Act
        var emptyIdResult = ProductType.Create(Guid.Empty, "Shoes");
        var emptyNameResult = ProductType.Create(Guid.NewGuid(), "");

        // Assert
        emptyIdResult.IsFailure.Should().BeTrue();
        emptyIdResult.Error.Should().Be(ProductTypeErrors.EmptyId);

        emptyNameResult.IsFailure.Should().BeTrue();
        emptyNameResult.Error.Should().Be(ProductTypeErrors.EmptyName);
    }

    [Fact]
    public void BaseEntity_MarkAsDeleted_Twice_ShouldReturnConflictError()
    {
        // Arrange
        var brand = ProductBrand.Create(Guid.NewGuid(), "Adidas").Value;

        // Act
        var firstDelete = brand.MarkAsDeleted();
        var secondDelete = brand.MarkAsDeleted();

        // Assert
        firstDelete.IsSuccess.Should().BeTrue();
        brand.IsDeleted.Should().BeTrue();
        brand.DeletedAt.Should().NotBeNull();

        secondDelete.IsFailure.Should().BeTrue();
        secondDelete.Error!.Type.Should().Be(ErrorType.Conflict);
    }
}
