using System.Text;
using ECommerce.Domain.Repositories;
using ECommerce.Infrastructure.Auth;
using ECommerce.Infrastructure.Baskets;
using ECommerce.Infrastructure.BackgroundJobs;
using ECommerce.Infrastructure.Caching;
using ECommerce.Infrastructure.Email;
using ECommerce.Infrastructure.Identity;
using ECommerce.Infrastructure.Interceptors;
using ECommerce.Infrastructure.Orders;
using ECommerce.Infrastructure.Payments;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Repositories;
using ECommerce.Infrastructure.Seeding;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Baskets;
using ECommerce.UseCases.Common.Caching;
using ECommerce.UseCases.Common.Email;
using ECommerce.UseCases.Common.Orders;
using ECommerce.UseCases.Common.Payments;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Resend;
using StackExchange.Redis;

namespace ECommerce.Infrastructure;

public static class DependancyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment = false,
        bool isTesting = false)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        var outboxSettings = configuration.GetSection(OutboxSettings.SectionName).Get<OutboxSettings>() ?? new();
        outboxSettings.Validate();
        services.Configure<OutboxSettings>(configuration.GetSection(OutboxSettings.SectionName));

        var hangfireSettings = configuration.GetSection(HangfireSettings.SectionName).Get<HangfireSettings>() ?? new();
        hangfireSettings.Validate();
        services.Configure<HangfireSettings>(configuration.GetSection(HangfireSettings.SectionName));

        services.AddSingleton<AuditableEntityInterceptor>();

        // 1. EF Core StoreDbContext
        services.AddDbContext<StoreDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.MigrationsAssembly(typeof(StoreDbContext).Assembly.FullName);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null);
                })
            .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());

            if (isDevelopment)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        // 2. ASP.NET Core Identity
        services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
        {
            // Applies to new registrations and password changes only;
            // existing password hashes are not affected.
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<StoreDbContext>()
        .AddDefaultTokenProviders();

        // 3. JWT Authentication & Options
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<GoogleAuthSettings>(configuration.GetSection(GoogleAuthSettings.SectionName));
        services.AddSingleton<IJwtProvider, JwtProvider>();

        // Google ID-token validation fetches signing certificates over HTTP.
        // This is the only real outbound HTTP dependency, so resilience is
        // scoped to that named client instead of a generic, unused one.
        services.AddHttpClient(GoogleTokenValidator.ResilientHttpClientName)
            .AddStandardResilienceHandler();
        services.AddSingleton<Google.Apis.Http.IHttpClientFactory, GoogleResilientHttpClientFactory>();

        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddScoped<IIdentityService, IdentityService>();

        var jwtSection = configuration.GetSection("Jwt");
        var secretKey = jwtSection.GetValue<string>("SecretKey");

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Set it in the root .env file for development or as an environment variable in production.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. Set it in the root .env file for development or as an environment variable in production.");
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
        services.AddResend(options =>
        {
            options.ApiToken = configuration["EmailSettings:ApiKey"] ?? string.Empty;
        });
        services.AddTransient<IEmailService, ResendEmailService>();
        services.AddKeyedScoped<IOutboxMessageHandler, EmailOutboxMessageHandler>("Email");

        // 4b. Stripe Payment
        services.Configure<StripeSettings>(configuration.GetSection(StripeSettings.SectionName));
        var stripeSettings = configuration.GetSection(StripeSettings.SectionName).Get<StripeSettings>() ?? new();
        var isDesignTime = string.Equals(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "ef", StringComparison.OrdinalIgnoreCase);
        if (!isTesting && !isDesignTime)
        {
            if (string.IsNullOrWhiteSpace(stripeSettings.SecretKey))
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");
            if (string.IsNullOrWhiteSpace(stripeSettings.WebhookSecret))
                throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");
        }
        services.AddScoped<IPaymentService, StripePaymentService>();
        services.AddScoped<IStockAtomicService, StockAtomicService>();
        services.AddScoped<IOrderLockingService, OrderLockingService>();

        // 5. Caching - Upstash Redis through IDistributedCache.
        var redisSettings = configuration.GetSection(RedisSettings.SectionName).Get<RedisSettings>() ?? new();
        redisSettings.Validate();
        services.Configure<RedisSettings>(configuration.GetSection(RedisSettings.SectionName));

        var redisOptions = redisSettings.ToConfigurationOptions();

        // One multiplexer for the process lifetime, used by the generation
        // counters, the cache service and the framework RedisCache alike.
        // The Lazy keeps startup non-blocking while guaranteeing one instance.
        var multiplexer = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer.Value);

        services.AddOptions<CacheSettings>()
            .Bind(configuration.GetSection(CacheSettings.SectionName));

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = redisOptions;
            options.InstanceName = redisSettings.InstanceName;
            options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer.Value);
        });

        services.AddHybridCache(options =>
        {
            var cacheSettings = configuration.GetSection(CacheSettings.SectionName).Get<CacheSettings>() ?? new();
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = cacheSettings.DistributedCacheExpiration,
                LocalCacheExpiration = cacheSettings.LocalCacheExpiration
            };
        });

        services.AddSingleton<ICacheService, HybridCacheService>();

        // 5b. Basket storage - Redis documents via the shared multiplexer.
        services.Configure<BasketSettings>(configuration.GetSection(BasketSettings.SectionName));
        var basketSettings = configuration.GetSection(BasketSettings.SectionName).Get<BasketSettings>() ?? new();
        basketSettings.Validate();
        services.AddSingleton<IBasketStore, RedisBasketStore>();
        services.AddSingleton(TimeProvider.System);

        // 6. Outbox processing infrastructure
        services.AddSingleton<IOutboxClock, SystemOutboxClock>();
        services.AddScoped<IOutboxMessageStore, EfCoreOutboxMessageStore>();

        // 7. Hangfire Background Processing
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfire(config =>
            {
                config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(hangfireSettings.CommandBatchMaxTimeoutMinutes),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(hangfireSettings.SlidingInvisibilityTimeoutMinutes),
                        QueuePollInterval = TimeSpan.FromSeconds(hangfireSettings.QueuePollIntervalSeconds),
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    });
            });

            services.AddHangfireServer(options =>
            {
                options.WorkerCount = hangfireSettings.WorkerCount;
                options.Queues = ["outbox", "default"];
                options.ServerTimeout = TimeSpan.FromSeconds(hangfireSettings.ServerTimeoutSeconds);
            });

            services.AddScoped<OutboxProcessorJob>();
            services.AddScoped<ReservationExpirationJob>();
            services.AddSingleton<IRecurringJobRegistrationState, RecurringJobRegistrationState>();

            if (!isTesting)
            {
                services.AddHostedService<RecurringJobRegistrationService>();
                services.AddHealthChecks()
                    .AddCheck<RecurringJobRegistrationHealthCheck>(
                        "hangfire-recurring-jobs",
                        tags: ["ready", "hangfire"]);
            }
        }

        // 8. Repositories & Seeding
        services.AddScoped(typeof(IReadRepository<>), typeof(Repository<>));
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IDataSeeder, IdentitySeeder>();
        services.AddScoped<IDataSeeder, ProductTypeSeeder>();
        services.AddScoped<IDataSeeder, ProductBrandSeeder>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
