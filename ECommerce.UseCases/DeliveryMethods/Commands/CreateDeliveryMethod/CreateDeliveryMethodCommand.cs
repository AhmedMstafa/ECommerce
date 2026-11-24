using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.DeliveryMethods.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.DeliveryMethods.Commands.CreateDeliveryMethod;

public sealed record CreateDeliveryMethodCommand(string Name, string? Description, decimal Price)
    : IRequest<Result<DeliveryMethodResponse>>;

public sealed class CreateDeliveryMethodCommandValidator : AbstractValidator<CreateDeliveryMethodCommand>
{
    public CreateDeliveryMethodCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateDeliveryMethodCommandHandler(IUnitOfWork uow)
    : IRequestHandler<CreateDeliveryMethodCommand, Result<DeliveryMethodResponse>>
{
    public async Task<Result<DeliveryMethodResponse>> Handle(CreateDeliveryMethodCommand request, CancellationToken cancellationToken)
    {
        var createResult = DeliveryMethod.Create(request.Name, request.Description, request.Price, DateTime.UtcNow);
        if (createResult.IsFailure)
            return Result.Failure<DeliveryMethodResponse>(createResult.Error!);

        var entity = createResult.Value;
        uow.AuditedRepository<DeliveryMethod>().Add(entity);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeliveryMethodResponse(entity.Id, entity.Name, entity.Description, entity.Price, entity.IsActive));
    }
}
