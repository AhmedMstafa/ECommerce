using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Auth.Commands.Login;

public record LoginCommand(string Email, string Password) : IRequest<Result<AuthResponse>>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public class LoginCommandHandler(IIdentityService identityService) 
    : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(
        LoginCommand request, 
        CancellationToken cancellationToken)
    {
        return await identityService.LoginAsync(
            request.Email, 
            request.Password, 
            cancellationToken);
    }
}
