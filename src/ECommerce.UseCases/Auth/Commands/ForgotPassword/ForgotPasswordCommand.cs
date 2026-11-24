using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Auth.Commands.ForgotPassword;

public record ForgotPasswordCommand(string Email) : IRequest<Result>;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");
    }
}

public class ForgotPasswordCommandHandler(IIdentityService identityService) 
    : IRequestHandler<ForgotPasswordCommand, Result>
{
    public async Task<Result> Handle(
        ForgotPasswordCommand request, 
        CancellationToken cancellationToken)
    {
        return await identityService.ForgotPasswordAsync(request.Email, cancellationToken);
    }
}
