using ECommerce.Domain.Data.DbContexts;
using ECommerce.Infrastructure.Interceptors;
using ECommerce.Infrastructure.Queries;
using ECommerce.Infrastructure.Seeding;
using ECommerce.UseCases.Brands;
using ECommerce.UseCases.Products;
using ECommerce.UseCases.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Infrastructure
{
    public static class DependancyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<AuditableEntityInterceptor>();

            services.AddDbContext<StoreDbContext>((serviceProvider, options) =>
            {
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sqlOptions => sqlOptions.MigrationsAssembly("ECommerce.Infrastructure"))
                .EnableSensitiveDataLogging()
                .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
            });

            services.AddScoped<IDataSeeder, ProductTypeSeeder>();
            services.AddScoped<IDataSeeder, ProductBrandSeeder>();

            services.AddScoped<DatabaseSeeder>();

            services.AddScoped<IProductQueryService, ProductQueryService>();
            services.AddScoped<IBrandQueryService, BrandQueryService>();
            services.AddScoped<ITypeQueryService, TypeQueryService>();

            return services;
        }
    }
}
