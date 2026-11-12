using ECommerce.Domain.Data.DbContexts;
using ECommerce.UseCases.Types;
using ECommerce.UseCases.Types.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Infrastructure.Queries;

public class TypeQueryService(StoreDbContext storeDbContext) : ITypeQueryService
{
    public async Task<IReadOnlyList<GetAllTypesResponse>> GetAllTypesAsync(CancellationToken cancellationToken = default)
    {
        return await storeDbContext.Types
            .Select(t => new GetAllTypesResponse
            {
                Id = t.Id,
                Name = t.Name
            })
            .ToListAsync(cancellationToken);
    }
}
