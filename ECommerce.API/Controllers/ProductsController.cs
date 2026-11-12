
using ECommerce.API.Models;
using ECommerce.UseCases.Products.Dtos;
using ECommerce.UseCases.Queries;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers
{
    /// <summary>
    /// Provides API endpoints for managing products.
    /// </summary>
    public class ProductsController(
        GetAllProductsQuery getAllProductsQuery,
        GetProductByIdQuery getProductByIdQuery) : ApiControllerBase
    {
        /// <summary>
        /// Retrieves all available products.
        /// </summary>
        /// <param name="cancellationToken">
        /// A token to cancel the request.
        /// </param>
        /// <returns>A list of products.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<GetAllProductsResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<GetAllProductsResponse>>> GetAll(
            CancellationToken cancellationToken = default)
        {
            var result = await getAllProductsQuery.ExecuteAsync(cancellationToken);

            return Ok(result);
        }

        /// <summary>
        /// Retrieves a product by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the product.</param>
        /// <param name="cancellationToken">
        /// A token to cancel the request.
        /// </param>
        /// <returns>The requested product if found.</returns>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<GetByIdProductResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ApiResponse<GetByIdProductResponse>>> GetById(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var result = await getProductByIdQuery.ExecuteAsync(id, cancellationToken);

            return FromResult(result);
        }
    }
}
