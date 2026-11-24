using Asp.Versioning.ApiExplorer;
using DotNetEnv;
using ECommerce.API;
using ECommerce.API.Hangfire;
using ECommerce.API.Health;
using ECommerce.Infrastructure;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Seeding;
using ECommerce.UseCases;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

// Load the repository-level .env before CreateBuilder reads configuration.
// Existing process environment variables always win: CI and the integration
// tests inject their own values. A missing .env file is not an error.
static string? FindEnvFile()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
         directory is not null;
         directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

var envFile = FindEnvFile();
if (envFile is not null)
{
    Env.NoClobber().Load(envFile);
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services);
});

builder.Services.AddPresentation(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment(), builder.Environment.IsEnvironment("Testing"));
builder.Services.AddApplication();

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) =>
    {
        if (exception is not null)
        {
            return LogEventLevel.Error;
        }

        return httpContext.Request.Path.StartsWithSegments("/health")
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    };

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
        diagnosticContext.Set("UserId", httpContext.User.Identity?.Name ?? "anonymous");
        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    };
});

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseCors("FrontendCors");
app.UseRateLimiter();

var swaggerEnabled = app.Configuration.GetValue<bool>("Swagger:Enabled", app.Environment.IsDevelopment());
if (swaggerEnabled)
{
    var apiVersionDescriptions = app.DescribeApiVersions();

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.ConfigObject.PersistAuthorization = true;

        foreach (var description in apiVersionDescriptions.Reverse())
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                $"ECommerce API {description.GroupName.ToUpperInvariant()}");
        }
    });
}

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbSeed = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var dbContext = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

    await dbContext.Database.MigrateAsync();
    await dbSeed.SeedAll();
}

var healthCheckOptions = new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    AllowCachingResponses = false
};

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = healthCheckOptions.ResponseWriter,
    AllowCachingResponses = false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = healthCheckOptions.ResponseWriter,
    AllowCachingResponses = false
});

app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    // The dashboard must see the authenticated principal, so it is registered
    // after the authentication/authorization middlewares.
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter()]
    });
}

app.MapControllers();

app.Run();

/// <summary>
/// Exposed for WebApplicationFactory-based integration tests.
/// </summary>
public partial class Program { }
