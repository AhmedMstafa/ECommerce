using Asp.Versioning;
using ECommerce.Domain.Common;
using ECommerce.UseCases.Orders.Commands.ProcessStripeWebhook;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[ApiVersionNeutral]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhooksController(ISender sender) : ControllerBase
{
    [HttpPost("stripe")]
    public async Task<IActionResult> Stripe(CancellationToken ct)
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync(ct);
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(signature))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Signature",
                Detail = "Missing Stripe-Signature header."
            });
        }

        var result = await sender.Send(new ProcessStripeWebhookCommand(json, signature), ct);

        if (result.IsSuccess)
            return Ok();

        if (result.Error?.Type == ErrorType.Validation)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Webhook",
                Detail = result.Error.Message
            });
        }

        // Return 500 so Stripe retries transient processing failures.
        return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Webhook Processing Failed",
            Detail = result.Error?.Message ?? "Webhook processing failed."
        });
    }
}