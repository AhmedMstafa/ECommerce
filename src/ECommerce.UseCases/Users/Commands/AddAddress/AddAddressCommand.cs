using ECommerce.Domain.Common;
using ECommerce.UseCases.Common.Auth;
using ECommerce.UseCases.Users.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.Users.Commands.AddAddress;

public record AddAddressCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string Street,
    string City,
    string State,
    string ZipCode,
    string Country,
    bool IsDefault) : IRequest<Result<AddressResponse>>;

public class AddAddressCommandValidator : AbstractValidator<AddAddressCommand>
{
    public AddAddressCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(256);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ZipCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

public class AddAddressCommandHandler(IIdentityService identityService)
    : IRequestHandler<AddAddressCommand, Result<AddressResponse>>
{
    public async Task<Result<AddressResponse>> Handle(AddAddressCommand request, CancellationToken cancellationToken)
    {
        return await identityService.AddAddressAsync(
            request.UserId, request.FirstName, request.LastName, request.Street,
            request.City, request.State, request.ZipCode, request.Country, request.IsDefault,
            cancellationToken);
    }
}
