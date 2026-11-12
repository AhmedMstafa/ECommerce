using ECommerce.API.Middlewares;
using System.Reflection;

namespace ECommerce.API
{
    public static class DependancyInjection
    {
        public static IServiceCollection AddPresentation(this IServiceCollection services)
        {
            services.AddControllers();
            services.AddProblemDetails();
            services.AddExceptionHandler<GlobalExceptionMiddleware>();

            services.AddSwaggerGen(options =>
            {
                var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
            });

            return services;
        }
    }
}
