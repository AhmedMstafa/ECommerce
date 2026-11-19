using Asp.Versioning;
using ECommerce.API.Models;
using ECommerce.UseCases.Products.Commands.CreateProduct;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Products.Queries;
using ECommerce.UseCases.Products.Specifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerce.API.Controllers;

/// <summary>
/// Provides API endpoints for managing products.
/// </summary>
[ApiVersion("1.0")]
[EnableRateLimiting("fixed")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public class ProductsController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Retrieves all available products.
    /// </summary>
    /// <param name="parameters">Filtering, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A list of products.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GetAllProductsResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GetAllProductsResponse>>> GetAll(
        [FromQuery] ProductSpecificationParameters? parameters,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetAllProductsQuery(parameters), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Retrieves a product by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the product.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The requested product if found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GetByIdProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<GetByIdProductResponse>>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetProductByIdQuery(id), cancellationToken);

        return FromResult(result);
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    /// <param name="command">The product creation details.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The ID of the newly created product.</returns>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<Guid>>> Create(
        [FromBody] CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return FromResult(result);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value },
            ApiResponse<Guid>.SuccessResponse(result.Value, StatusCodes.Status201Created));
    }
}
