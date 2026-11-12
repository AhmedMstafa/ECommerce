using ECommerce.UseCases.Queries;

namespace ECommerce.API.EndPoints
{
    public static class ProductEndpoints
    {
        public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var group = endpoints.MapGroup("/api/products").WithTags("Products");

            group.MapGet("/", async (GetAllProductsQuery query, CancellationToken cancellationToken) =>
            {
                var products = await query.ExecuteAsync(cancellationToken);
                return Results.Ok(products);
            });

           
            return endpoints;
        }
    }
}
