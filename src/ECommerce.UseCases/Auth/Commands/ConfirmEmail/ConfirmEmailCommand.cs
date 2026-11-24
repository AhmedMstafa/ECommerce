using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Auth.Commands.ConfirmEmail;

public record ConfirmEmailCommand(Guid UserId, string Token) : IRequest<Result>;

public class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Confirmation token is required.");
    }
}

public class ConfirmEmailCommandHandler(IIdentityService identityService)
    : IRequestHandler<ConfirmEmailCommand, Result>
{
    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        return await identityService.ConfirmEmailAsync(request.UserId, request.Token, cancellationToken);
    }
}
