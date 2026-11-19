using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ECommerce.API.Swagger;

/// <summary>
/// Makes the generated OpenAPI document reflect the real authorization
/// requirements: anonymous endpoints advertise no security, protected
/// endpoints advertise bearer auth and their 401/403 responses.
/// </summary>
public sealed class SecurityRequirementsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var isAnonymous = endpointMetadata.OfType<AllowAnonymousAttribute>().Any();
        var isProtected = endpointMetadata.OfType<AuthorizeAttribute>().Any() ||
                          context.ApiDescription.ActionDescriptor is ControllerActionDescriptor;

        if (isAnonymous)
        {
            operation.Security = [];
            return;
        }

        // Controller APIs are protected by the global fallback policy unless
        // explicitly marked [AllowAnonymous].
        if (!isProtected)
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
            }
        ];

        operation.Responses["401"] = new OpenApiResponse { Description = "Authentication is required." };
        operation.Responses["403"] = new OpenApiResponse { Description = "The authenticated user is not authorized." };
    }
}
