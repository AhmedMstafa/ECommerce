namespace ECommerce.Domain.Specifications.Builders;

public class SpecificationBuilder<T>(BaseSpecification<T> specification) : ISpecificationBuilder<T> where T : class
{
    public BaseSpecification<T> Specification { get; } = specification;
}

public class IncludableSpecificationBuilder<T, TProperty>(BaseSpecification<T> specification)
    : SpecificationBuilder<T>(specification), IIncludableSpecificationBuilder<T, TProperty> where T : class
{
}
