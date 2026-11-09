using ECommerce.Domain.Data.DbContexts;
using ECommerce.Infrastructure.Interceptors;
using ECommerce.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Infrastructure
{
    public static class DependancyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services,IConfiguration configuration)
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

            return services;
        }
    }
}
