using Asp.Versioning.ApiExplorer;
using ECommerce.API;
using ECommerce.API.Hangfire;
using ECommerce.Infrastructure;
using ECommerce.Infrastructure.BackgroundJobs;
using ECommerce.Infrastructure.Persistence;
using ECommerce.Infrastructure.Seeding;
using ECommerce.UseCases;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddPresentation(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddApplication();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    var apiVersionDescriptions = app.DescribeApiVersions();

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
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

// Hangfire Dashboard & Background Job Scheduling
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()]
});

RecurringJob.AddOrUpdate<OutboxProcessorJob>(
    "process-outbox-messages",
    job => job.ProcessPendingMessagesAsync(CancellationToken.None),
    "*/15 * * * * *");

// Health check endpoints: Liveness and Readiness
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
