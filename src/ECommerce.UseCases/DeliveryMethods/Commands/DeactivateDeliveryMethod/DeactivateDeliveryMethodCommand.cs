using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.DeliveryMethods.Dtos;
using MediatR;

namespace ECommerce.UseCases.DeliveryMethods.Commands.DeactivateDeliveryMethod;

public sealed record DeactivateDeliveryMethodCommand(Guid Id) : IRequest<Result<DeliveryMethodResponse>>;

public sealed class DeactivateDeliveryMethodCommandHandler(IUnitOfWork uow)
    : IRequestHandler<DeactivateDeliveryMethodCommand, Result<DeliveryMethodResponse>>
{
    public async Task<Result<DeliveryMethodResponse>> Handle(DeactivateDeliveryMethodCommand request, CancellationToken cancellationToken)
    {
        var entity = await uow.AuditedRepository<DeliveryMethod>().GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            return Result.Failure<DeliveryMethodResponse>(DeliveryMethodErrors.NotFound(request.Id));

        var result = entity.Deactivate(DateTime.UtcNow);
        if (result.IsFailure)
            return Result.Failure<DeliveryMethodResponse>(result.Error!);

        uow.AuditedRepository<DeliveryMethod>().Update(entity);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeliveryMethodResponse(entity.Id, entity.Name, entity.Description, entity.Price, entity.IsActive));
    }
}
