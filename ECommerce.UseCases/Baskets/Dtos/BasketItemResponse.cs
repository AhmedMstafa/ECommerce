namespace ECommerce.UseCases.Baskets.Dtos;

/// <summary>
/// A basket line priced from current SQL data. Redis only remembers the
/// product id, quantity and the optional price seen when the item was added.
/// </summary>
public sealed record BasketItemResponse
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = default!;

    public string PictureUrl { get; init; } = default!;

    public int Quantity { get; init; }

    public decimal? PriceAtAdd { get; init; }

    public decimal CurrentUnitPrice { get; init; }

    public bool HasPriceChanged { get; init; }

    public decimal PriceDifference { get; init; }

    public decimal LineTotal { get; init; }

    public int AvailableQuantity { get; init; }

    /// <summary>Available, OutOfStock, Unavailable, or QuantityReduced.</summary>
    public string Status { get; init; } = default!;

    public bool CanCheckout { get; init; }
}
