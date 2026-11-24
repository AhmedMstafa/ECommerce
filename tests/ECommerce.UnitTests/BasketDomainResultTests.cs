using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class BasketDomainResultTests
{
    [Fact]
    public void BasketItem_Create_ShouldReturnFailure_ForInvalidQuantity()
    {
        var result = BasketItem.Create(Guid.NewGuid(), 0, 10m, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(BasketErrors.InvalidQuantity.Code);
    }

    [Fact]
    public void BasketItem_Create_ShouldReturnFailure_ForEmptyProductId()
    {
        var result = BasketItem.Create(Guid.Empty, 1, 10m, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Basket_Remove_ShouldReturnFailure_WhenItemMissing()
    {
        var basket = Basket.CreateGuest("hash", DateTime.UtcNow);
        var missing = Guid.NewGuid();

        var result = basket.Remove(missing, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(BasketErrors.ItemNotFound(missing).Code);
    }

    [Fact]
    public void Basket_SetQuantity_ShouldReturnFailure_WhenItemMissing()
    {
        var basket = Basket.CreateGuest("hash", DateTime.UtcNow);

        var result = basket.SetQuantity(Guid.NewGuid(), 2, 10m, DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }
}
