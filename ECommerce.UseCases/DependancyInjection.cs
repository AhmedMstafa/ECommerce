using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.UseCases
{
    public static class DependancyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {

            return services;
        }
    }
}
