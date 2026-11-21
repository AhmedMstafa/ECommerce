using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.UseCases.Baskets.Dtos;

namespace ECommerce.UseCases.Common.Baskets;

/// <summary>
/// Application rules for baskets. Controllers only supply identities and
/// validated primitive input; all pricing, stock and merge decisions happen here.
/// </summary>
public interface IBasketService
{
    Task<Result<BasketResponse>> GetAsync(BasketKey key, CancellationToken ct = default);

    Task<Result<BasketResponse>> AddItemAsync(
        BasketKey key,
        Guid productId,
        int quantity,
        CancellationToken ct = default);

    Task<Result<BasketResponse>> RemoveItemAsync(
        BasketKey key,
        Guid productId,
        CancellationToken ct = default);

    Task<Result<BasketResponse>> UpdateQuantityAsync(
        BasketKey key,
        Guid productId,
        int quantity,
        CancellationToken ct = default);

    Task<Result<BasketResponse>> ClearAsync(BasketKey key, CancellationToken ct = default);

    Task<Result<BasketResponse>> MergeAsync(
        BasketKey userKey,
        string guestToken,
        CancellationToken ct = default);
}
