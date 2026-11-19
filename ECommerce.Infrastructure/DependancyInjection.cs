using System.Text;
using ECommerce.Domain.Repositories;
using ECommerce.Infrastructure.Auth;
using ECommerce.Infrastructure.BackgroundJobs;
using ECommerce.Infrastructure.Caching;
using ECommerce.Infrastructure.Email;
using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Interceptors;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Repositories;
using ECommerce.Infrastructure.Seeding;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Common.Email;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ECommerce.Infrastructure;

public static class DependancyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, 
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddSingleton<AuditableEntityInterceptor>();

        // 1. EF Core StoreDbContext
        services.AddDbContext<StoreDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(StoreDbContext).Assembly.FullName))
            .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());

            if (isDevelopment)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        // 2. ASP.NET Core Identity
        services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<StoreDbContext>()
        .AddDefaultTokenProviders();

        // 3. JWT Authentication & Options
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddSingleton<IJwtProvider, JwtProvider>();
        services.AddScoped<IIdentityService, IdentityService>();

        var jwtSection = configuration.GetSection("Jwt");
        var secretKey = jwtSection.GetValue<string>("SecretKey");

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Use user-secrets in development and an environment variable in production.");
        }

        var issuer = jwtSection.GetValue<string>("Issuer") ?? "ECommerceApi";
        var audience = jwtSection.GetValue<string>("Audience") ?? "ECommerceClient";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
            };
        });

        // 4. Email Services & Settings
        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        services.AddTransient<IEmailService, SmtpEmailService>();
        services.AddKeyedScoped<IOutboxMessageHandler, EmailOutboxMessageHandler>("Email");

        // 5. Caching
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();

        // 6. Resilience
        services.AddHttpClient("ExternalServices")
            .AddStandardResilienceHandler();

        // 7. Hangfire Background Processing
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddHangfire(config =>
            {
                config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.FromSeconds(15),
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    });
            });

            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Math.Max(2, Environment.ProcessorCount);
            });

            services.AddScoped<OutboxProcessorJob>();
        }

        // 8. Repositories & Seeding
        services.AddScoped(typeof(IReadRepository<>), typeof(Repository<>));
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IDataSeeder, ProductTypeSeeder>();
        services.AddScoped<IDataSeeder, ProductBrandSeeder>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
