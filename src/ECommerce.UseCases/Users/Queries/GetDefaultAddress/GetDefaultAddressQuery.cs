using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Users.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Users.Queries.GetDefaultAddress;

public record GetDefaultAddressQuery(Guid UserId) : IRequest<Result<AddressResponse>>;

public class GetDefaultAddressQueryValidator : AbstractValidator<GetDefaultAddressQuery>
{
    public GetDefaultAddressQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class GetDefaultAddressQueryHandler(IIdentityService identityService)
    : IRequestHandler<GetDefaultAddressQuery, Result<AddressResponse>>
{
    public async Task<Result<AddressResponse>> Handle(GetDefaultAddressQuery request, CancellationToken cancellationToken)
    {
        return await identityService.GetDefaultAddressAsync(request.UserId, cancellationToken);
    }
}
