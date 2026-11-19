using ECommerce.UseCases.Products.Queries;
using MediatR;

namespace ECommerce.API.EndPoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
        {
            var products = await sender.Send(new GetAllProductsQuery(), cancellationToken);
            return Results.Ok(products);
        });

        return endpoints;
    }
}
