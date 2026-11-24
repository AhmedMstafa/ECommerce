using ECommerce.UseCases.Products.Specifications;
using FluentValidation;

namespace ECommerce.UseCases.Products.Queries;

public class GetAllProductsQueryValidator : AbstractValidator<GetAllProductsQuery>
{
    public GetAllProductsQueryValidator()
    {
        RuleFor(x => x.Parameters!)
            .SetValidator(new ProductSpecificationParametersValidator())
            .When(x => x.Parameters is not null);
    }
}
