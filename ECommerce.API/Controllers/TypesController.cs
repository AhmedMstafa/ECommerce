using Asp.Versioning;
using ECommerce.UseCases.Types.Dtos;
using ECommerce.UseCases.Types.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

[ApiVersion("1.0")]
[EnableRateLimiting("fixed")]
public class TypesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GetAllTypesResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GetAllTypesResponse>>> GetAll(CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAllTypesQuery(), cancellationToken);

        return Ok(result);
    }
}
