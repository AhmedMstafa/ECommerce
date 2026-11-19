using System.Globalization;
using System.Reflection;
using System.Threading.RateLimiting;
using Asp.Versioning;
using ECommerce.API.Configuration;
using ECommerce.API.Health;
using ECommerce.API.Middlewares;
using ECommerce.API.Swagger;
using ECommerce.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ECommerce.API;

public static class DependancyInjection
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionMiddleware>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
        });

        // 1. API Versioning
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        // 2. Swagger with Versioning
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        services.AddSwaggerGen(options =>
        {
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = JwtBearerDefaults.AuthenticationScheme,
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter the JWT received from /api/v1/auth/login."
            });

            // OpenAPI.NET v2 requires the document-aware reference type.
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
            });

            options.OperationFilter<SecurityRequirementsOperationFilter>();
        });

        // 3. Rate Limiting
        services.Configure<RateLimitingSettings>(configuration.GetSection("RateLimiting"));
        var rateLimiting = configuration.GetSection("RateLimiting").Get<RateLimitingSettings>() ?? new();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("fixed", context => RateLimitPartition.GetFixedWindowLimiter(
                GetRateLimitPartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, rateLimiting.Fixed.PermitLimit),
                    Window = TimeSpan.FromSeconds(Math.Max(1, rateLimiting.Fixed.WindowSeconds)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = Math.Max(0, rateLimiting.Fixed.QueueLimit)
                }));

            options.AddPolicy("auth-policy", context => RateLimitPartition.GetFixedWindowLimiter(
                GetRateLimitPartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, rateLimiting.Auth.PermitLimit),
                    Window = TimeSpan.FromSeconds(Math.Max(1, rateLimiting.Auth.WindowSeconds)),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = Math.Max(0, rateLimiting.Auth.QueueLimit)
                }));

            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "You have exceeded your request quota. Please wait before retrying."
                };

                await context.HttpContext.Response.WriteAsJsonAsync(
                    problemDetails,
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: token);
            };
        });

        // 4. Health Checks
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var healthChecks = services.AddHealthChecks();

        // Always register a self check so readiness cannot report healthy with
        // an empty registration list when configuration is missing.
        healthChecks.AddCheck(
            "self",
            () => HealthCheckResult.Healthy("API process is running."),
            tags: ["self", "ready"]);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            healthChecks.AddSqlServer(
                connectionString: connectionString,
                name: "sqlserver",
                tags: ["db", "ready"],
                timeout: TimeSpan.FromSeconds(5));
        }

        return services;
    }

    private static string GetRateLimitPartitionKey(HttpContext context)
    {
        return context.User.Identity?.Name ??
               context.Connection.RemoteIpAddress?.ToString() ??
               "anonymous";
    }
}
