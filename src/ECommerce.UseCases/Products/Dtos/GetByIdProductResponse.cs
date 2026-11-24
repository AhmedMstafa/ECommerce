namespace ECommerce.UseCases.Products.Dtos;

public record GetByIdProductResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Description { get; init; } = default!;
    public string PictureUrl { get; init; } = default!;
    public decimal Price { get; init; }
    public string ProductBrand { get; init; } = default!;
    public string ProductType { get; init; } = default!;
}
