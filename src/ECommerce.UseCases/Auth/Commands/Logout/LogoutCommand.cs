using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Auth.Commands.Logout;

public record LogoutCommand(Guid UserId, string RefreshToken) : IRequest<Result>;

public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User is required.");
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.");
    }
}

public class LogoutCommandHandler(IIdentityService identityService)
    : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        return await identityService.LogoutAsync(request.UserId, request.RefreshToken, cancellationToken);
    }
}
