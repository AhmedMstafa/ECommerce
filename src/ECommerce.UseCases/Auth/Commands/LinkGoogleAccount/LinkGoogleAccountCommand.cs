using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Auth.Commands.LinkGoogleAccount;

/// <summary>
/// Explicit account-linking command. The user id comes from the authenticated
/// principal, never from the request body, so it cannot be spoofed by a caller.
/// </summary>
public record LinkGoogleAccountCommand(string IdToken, Guid AuthenticatedUserId)
    : IRequest<Result>;

public class LinkGoogleAccountCommandValidator : AbstractValidator<LinkGoogleAccountCommand>
{
    public LinkGoogleAccountCommandValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty().WithMessage("Google ID token is required.");
        RuleFor(x => x.AuthenticatedUserId).NotEmpty().WithMessage("An authenticated user is required.");
    }
}

public class LinkGoogleAccountCommandHandler(IIdentityService identityService)
    : IRequestHandler<LinkGoogleAccountCommand, Result>
{
    public async Task<Result> Handle(
        LinkGoogleAccountCommand request,
        CancellationToken cancellationToken)
    {
        return await identityService.LinkGoogleAccountAsync(
            request.IdToken,
            request.AuthenticatedUserId,
            cancellationToken);
    }
}
