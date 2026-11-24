using ECommerce.Domain.Common;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Repositories;
using ECommerce.Domain.Specifications;
using ECommerce.UseCases.DeliveryMethods.Dtos;
using MediatR;

namespace ECommerce.UseCases.DeliveryMethods.Queries.GetActiveDeliveryMethods;

public sealed record GetActiveDeliveryMethodsQuery : IRequest<Result<IReadOnlyList<DeliveryMethodResponse>>>;

public sealed class ActiveDeliveryMethodsSpecification : BaseSpecification<DeliveryMethod>
{
    public ActiveDeliveryMethodsSpecification()
    {
        AddCriteria(x => x.IsActive);
        AddOrderBy(x => x.Price);
    }
}

public sealed class GetActiveDeliveryMethodsQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetActiveDeliveryMethodsQuery, Result<IReadOnlyList<DeliveryMethodResponse>>>
{
    public async Task<Result<IReadOnlyList<DeliveryMethodResponse>>> Handle(GetActiveDeliveryMethodsQuery request, CancellationToken cancellationToken)
    {
        var methods = await uow.AuditedReadRepository<DeliveryMethod>()
            .GetAllWithSpecAsync(new ActiveDeliveryMethodsSpecification(), cancellationToken);

        var response = methods.Select(m => new DeliveryMethodResponse(m.Id, m.Name, m.Description, m.Price, m.IsActive)).ToList();
        return Result.Success<IReadOnlyList<DeliveryMethodResponse>>(response);
    }
}
