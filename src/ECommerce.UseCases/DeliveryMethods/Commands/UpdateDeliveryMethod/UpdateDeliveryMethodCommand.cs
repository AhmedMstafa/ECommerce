using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.UseCases.DeliveryMethods.Dtos;
using FluentValidation;
using MediatR;

namespace ECommerce.UseCases.DeliveryMethods.Commands.UpdateDeliveryMethod;

public sealed record UpdateDeliveryMethodCommand(Guid Id, string Name, string? Description, decimal Price)
    : IRequest<Result<DeliveryMethodResponse>>;

public sealed class UpdateDeliveryMethodCommandValidator : AbstractValidator<UpdateDeliveryMethodCommand>
{
    public UpdateDeliveryMethodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateDeliveryMethodCommandHandler(IUnitOfWork uow)
    : IRequestHandler<UpdateDeliveryMethodCommand, Result<DeliveryMethodResponse>>
{
    public async Task<Result<DeliveryMethodResponse>> Handle(UpdateDeliveryMethodCommand request, CancellationToken cancellationToken)
    {
        var entity = await uow.AuditedRepository<DeliveryMethod>().GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            return Result.Failure<DeliveryMethodResponse>(DeliveryMethodErrors.NotFound(request.Id));

        var updateResult = entity.Update(request.Name, request.Description, request.Price, DateTime.UtcNow);
        if (updateResult.IsFailure)
            return Result.Failure<DeliveryMethodResponse>(updateResult.Error!);

        uow.AuditedRepository<DeliveryMethod>().Update(entity);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeliveryMethodResponse(entity.Id, entity.Name, entity.Description, entity.Price, entity.IsActive));
    }
}
