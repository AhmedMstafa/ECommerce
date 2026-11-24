using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Auth.Commands.GoogleLogin;

public record GoogleLoginCommand(string IdToken) : IRequest<Result<AuthResponse>>;

public class GoogleLoginCommandValidator : AbstractValidator<GoogleLoginCommand>
{
    public GoogleLoginCommandValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty().WithMessage("Google ID token is required.");
    }
}

public class GoogleLoginCommandHandler(IIdentityService identityService) 
    : IRequestHandler<GoogleLoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(
        GoogleLoginCommand request, 
        CancellationToken cancellationToken)
    {
        return await identityService.GoogleLoginAsync(request.IdToken, cancellationToken);
    }
}
