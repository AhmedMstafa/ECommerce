using FluentValidation;

namespace ECommerce.UseCases.Products.Commands.CreateProduct;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(200).WithMessage("Product name cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Product description is required.")
            .MaximumLength(2000).WithMessage("Product description cannot exceed 2000 characters.");

        RuleFor(x => x.PictureUrl)
            .NotEmpty().WithMessage("Product picture URL is required.")
            .MaximumLength(500).WithMessage("Picture URL cannot exceed 500 characters.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("Product price cannot be negative.");

        RuleFor(x => x.ProductBrandId)
            .NotEmpty().WithMessage("Brand ID is required.");

        RuleFor(x => x.ProductTypeId)
            .NotEmpty().WithMessage("Type ID is required.");
    }
}
