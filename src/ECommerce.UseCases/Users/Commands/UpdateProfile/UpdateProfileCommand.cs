using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Common.Models;
using ECommerce.UseCases.Users.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Users.Commands.UpdateProfile;

public record UpdateProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    Optional<string?> Description,
    string? PictureUrl) : IRequest<Result<UserProfileResponse>>;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User is required.");
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.").MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.").MaximumLength(100);
        RuleFor(x => x.Description.Value)
            .MaximumLength(1000)
            .When(x => x.Description.HasValue && x.Description.Value is not null)
            .WithMessage("Description cannot exceed 1000 characters.");
        RuleFor(x => x.PictureUrl)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.PictureUrl))
            .WithMessage("Picture URL cannot exceed 500 characters.");
    }
}

public class UpdateProfileCommandHandler(IIdentityService identityService)
    : IRequestHandler<UpdateProfileCommand, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        return await identityService.UpdateProfileAsync(
            request.UserId,
            request.FirstName,
            request.LastName,
            request.Description,
            request.PictureUrl,
            cancellationToken);
    }
}
