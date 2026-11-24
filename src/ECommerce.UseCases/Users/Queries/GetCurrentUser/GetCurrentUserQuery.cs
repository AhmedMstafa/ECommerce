using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Users.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Users.Queries.GetCurrentUser;

public record GetCurrentUserQuery(Guid UserId) : IRequest<Result<UserProfileResponse>>;

public class GetCurrentUserQueryValidator : AbstractValidator<GetCurrentUserQuery>
{
    public GetCurrentUserQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User is required.");
    }
}

public class GetCurrentUserQueryHandler(IIdentityService identityService)
    : IRequestHandler<GetCurrentUserQuery, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        return await identityService.GetCurrentUserAsync(request.UserId, cancellationToken);
    }
}
