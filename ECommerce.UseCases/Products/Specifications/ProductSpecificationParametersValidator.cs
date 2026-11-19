using FluentValidation;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductSpecificationParametersValidator : AbstractValidator<ProductSpecificationParameters>
{
    public ProductSpecificationParametersValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThan(0).WithMessage("Page index must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ProductSpecificationParameters.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {ProductSpecificationParameters.MaxPageSize}.");

        RuleFor(x => x.Search)
            .MaximumLength(ProductSpecificationParameters.MaxSearchLength)
            .WithMessage($"Search cannot exceed {ProductSpecificationParameters.MaxSearchLength} characters.");

        RuleFor(x => x.Sort)
            .Must(sort => string.IsNullOrWhiteSpace(sort) || ProductSortOptions.Allowed.Contains(sort))
            .WithMessage($"Sort must be one of: {string.Join(", ", ProductSortOptions.Allowed)}.");
    }
}
