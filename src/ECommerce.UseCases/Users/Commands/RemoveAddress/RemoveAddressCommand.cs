using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Users.Commands.RemoveAddress;

public record RemoveAddressCommand(Guid UserId, Guid AddressId) : IRequest<Result>;

public class RemoveAddressCommandValidator : AbstractValidator<RemoveAddressCommand>
{
    public RemoveAddressCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.AddressId).NotEmpty();
    }
}

public class RemoveAddressCommandHandler(IIdentityService identityService)
    : IRequestHandler<RemoveAddressCommand, Result>
{
    public async Task<Result> Handle(RemoveAddressCommand request, CancellationToken cancellationToken)
    {
        return await identityService.RemoveAddressAsync(request.UserId, request.AddressId, cancellationToken);
    }
}
