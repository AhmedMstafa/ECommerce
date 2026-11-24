namespace ECommerce.UseCases.Types.Dtos;

public record GetAllTypesResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
}
