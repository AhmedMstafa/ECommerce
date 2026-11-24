namespace ECommerce.UseCases.Baskets.Dtos;

public sealed record BasketResponse
{
    public Guid Id { get; init; }

    public Guid? UserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    public IReadOnlyList<BasketItemResponse> Items { get; init; } = [];

    public int TotalItems { get; init; }

    public decimal Subtotal { get; init; }

    public bool HasUnavailableItems { get; init; }

    public bool HasPriceChanges { get; init; }
}
