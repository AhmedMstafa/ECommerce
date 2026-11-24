using Asp.Versioning;
using ECommerce.UseCases.Brands.Dtos;
using ECommerce.UseCases.Brands.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

[ApiVersion("1.0")]
[EnableRateLimiting("fixed")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class BrandsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GetAllBrandsResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GetAllBrandsResponse>>> GetAll(CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAllBrandsQuery(), cancellationToken);

        return Ok(result);
    }
}
