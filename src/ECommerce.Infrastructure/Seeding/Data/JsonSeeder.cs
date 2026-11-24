using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ECommerce.Infrastructure.Seeding.Data
{
    public class JsonSeeder
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static async Task<IReadOnlyList<TModel>> ReadAsync<TModel>(string fileName, CancellationToken ct)
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "Seeding", "Data", fileName);

            if (!File.Exists(filePath))
            {
                return [];
            }

            await using var stream = File.OpenRead(filePath);
            var models = await JsonSerializer.DeserializeAsync<List<TModel>>(stream, _options, ct);
            return models ?? [];
        }

        public static async Task SeedIfEmpty<TEntity, TModel>(
            DbSet<TEntity> dbset,
            string fileName,
            Func<TModel, TEntity> map,
            CancellationToken ct) where TEntity : BaseEntity
        {
            if (await dbset.AnyAsync(ct))
            {
                return;
            }

            var models = await ReadAsync<TModel>(fileName, ct);

            if (models.Count == 0)
            {
                return;
            }

            await dbset.AddRangeAsync(models.Select(map), ct);
        }
    }
}
