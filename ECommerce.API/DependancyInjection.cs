using ECommerce.API.Middlewares;

namespace ECommerce.API
{
    public static class DependancyInjection
    {
        public static IServiceCollection AddPresentation(this IServiceCollection services)
        {
            services.AddControllers();
            services.AddProblemDetails();
            services.AddExceptionHandler<GlobalExceptionMiddleware>();

            return services;
        }
    }
}
