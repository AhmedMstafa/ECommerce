using FluentValidation;

namespace ECommerce.UseCases.Products.Specifications;

public class ProductSpecificationParametersValidator : AbstractValidator<ProductSpecificationParameters>
{
    public ProductSpecificationParametersValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, ProductSpecificationParameters.MaxPageSize)
            .WithMessage($"Page size must be between 1 and {ProductSpecificationParameters.MaxPageSize}.");

        RuleFor(x => x.Search)
            .MaximumLength(ProductSpecificationParameters.MaxSearchLength)
            .WithMessage($"Search cannot exceed {ProductSpecificationParameters.MaxSearchLength} characters.");

        RuleFor(x => x.Sort)
            .Must(sort => string.IsNullOrWhiteSpace(sort) || ProductSortOptions.Allowed.Contains(sort))
            .WithMessage($"Sort must be one of: {string.Join(", ", ProductSortOptions.Allowed)}.");

        RuleFor(x => x.SortBy)
            .Must(field => string.IsNullOrWhiteSpace(field) || ProductSortOptions.AllowedFields.Contains(field))
            .WithMessage($"SortBy must be one of: {string.Join(", ", ProductSortOptions.AllowedFields)}.");

        RuleFor(x => x.SortOrder)
            .Must(order => string.IsNullOrWhiteSpace(order) || ProductSortOptions.AllowedDirections.Contains(order))
            .WithMessage($"SortOrder must be one of: {string.Join(", ", ProductSortOptions.AllowedDirections)}.");

        RuleFor(x => x.MinPrice)
            .GreaterThanOrEqualTo(0).WithMessage("MinPrice cannot be negative.");

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(0).WithMessage("MaxPrice cannot be negative.");

        RuleFor(x => x)
            .Must(parameters => !parameters.MinPrice.HasValue ||
                                !parameters.MaxPrice.HasValue ||
                                parameters.MinPrice <= parameters.MaxPrice)
            .WithMessage("MinPrice cannot be greater than MaxPrice.")
            .WithName("PriceRange");
    }
}
