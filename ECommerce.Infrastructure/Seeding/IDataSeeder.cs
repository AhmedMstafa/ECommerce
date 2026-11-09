namespace ECommerce.Infrastructure.Seeding
{
    public interface IDataSeeder
    {
        int Order { get; }
        Task SeedAsync(CancellationToken cancellationToken = default);
    }
}
