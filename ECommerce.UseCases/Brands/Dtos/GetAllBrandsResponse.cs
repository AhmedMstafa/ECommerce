namespace ECommerce.UseCases.Brands.Dtos;

public record GetAllBrandsResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
}
