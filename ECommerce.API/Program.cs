using ECommerce.API;
using ECommerce.Domain.Data.DbContexts;
using ECommerce.Infrastructure;
using ECommerce.Infrastructure.Seeding;
using ECommerce.UseCases;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPresentation();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddApplication();

var app = builder.Build();

app.UseExceptionHandler();

//app.MapGet("/error", () =>
//{
//    throw new Exception("Something went wrong internally!");
//});

if (app.Environment.IsDevelopment())
{

var scope = app.Services.CreateAsyncScope();
var dbSeed = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var dbContext = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

    await dbContext.Database.MigrateAsync();
    await dbSeed.SeedAll();
}

app.Run();