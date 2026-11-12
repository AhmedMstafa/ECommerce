using ECommerce.UseCases.Brands.Dtos;
using ECommerce.UseCases.Queries;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers
{
    public class BrandsController(GetAllBrandsQuery getAllBrandsQuery) : ApiControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<GetAllBrandsResponse>>> GetAll(CancellationToken cancellationToken = default)
        {
            var result = await getAllBrandsQuery.ExecuteAsync(cancellationToken);

            return Ok(result);
        }
    }
}
