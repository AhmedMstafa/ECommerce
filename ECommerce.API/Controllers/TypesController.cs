using ECommerce.UseCases.Queries;
using ECommerce.UseCases.Types.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers
{
    public class TypesController(GetAllTypesQuery getAllTypesQuery) : ApiControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<GetAllTypesResponse>>> GetAll(CancellationToken cancellationToken = default)
        {
            var result = await getAllTypesQuery.ExecuteAsync(cancellationToken);

            return Ok(result);
        }
    }
}
