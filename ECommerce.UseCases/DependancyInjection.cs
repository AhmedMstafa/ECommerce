using System.Reflection;
using ECommerce.UseCases.Queries;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.UseCases;

public static class DependancyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetAllProductsQuery>();
        services.AddScoped<GetProductByIdQuery>();
        services.AddScoped<GetAllBrandsQuery>();
        services.AddScoped<GetAllTypesQuery>();

        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(Assembly.GetExecutingAssembly());
        services.AddSingleton(config);
        services.AddMapster();

        return services;
    }
}
