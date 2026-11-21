using System.Globalization;
using System.Reflection;
using System.Threading.RateLimiting;
using Asp.Versioning;
using ECommerce.API.Configuration;
using ECommerce.API.Filters;
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
        services.AddScoped<ActionLoggingFilter>();
        services.AddControllers(options =>
        {
            options.Filters.AddService<ActionLoggingFilter>();
        });
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
            // Every controller route carries an explicit version segment.
            options.AssumeDefaultVersionWhenUnspecified = false;
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

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("fixed", context =>
            {
                var settings = GetRateLimitingSettings(context);
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, settings.Fixed.PermitLimit),
                        Window = TimeSpan.FromSeconds(Math.Max(1, settings.Fixed.WindowSeconds)),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = Math.Max(0, settings.Fixed.QueueLimit)
                    });
            });

            options.AddPolicy("auth-policy", context =>
            {
                var settings = GetRateLimitingSettings(context);
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, settings.Auth.PermitLimit),
                        Window = TimeSpan.FromSeconds(Math.Max(1, settings.Auth.WindowSeconds)),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = Math.Max(0, settings.Auth.QueueLimit)
                    });
            });

            options.AddPolicy("basket-policy", context =>
            {
                var settings = GetRateLimitingSettings(context);
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetBasketRateLimitPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, settings.Basket.PermitLimit),
                        Window = TimeSpan.FromSeconds(Math.Max(1, settings.Basket.WindowSeconds)),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = Math.Max(0, settings.Basket.QueueLimit)
                    });
            });

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

    private static string GetBasketRateLimitPartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"basket:user:{userId}";
        }

        // For unauthenticated guests, prefer a derived key from the guest token
        // so limits are not shared across all guests behind the same IP. The raw
        // token is never trusted as identity; only a hash of a syntactically
        // valid token is used. If absent/invalid, fall back to client IP.
        var guestToken = context.Request.Headers["X-Guest-Token"].ToString();
        if (string.IsNullOrWhiteSpace(guestToken))
        {
            guestToken = context.Request.Cookies["guest_basket_token"];
        }

        if (ECommerce.UseCases.Common.Baskets.GuestToken.IsValid(guestToken))
        {
            return $"basket:guest:{ECommerce.UseCases.Common.Baskets.GuestToken.Hash(guestToken!)}";
        }

        return $"basket:ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static RateLimitingSettings GetRateLimitingSettings(HttpContext context) =>
        context.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("RateLimiting")
            .Get<RateLimitingSettings>() ?? new();
}
