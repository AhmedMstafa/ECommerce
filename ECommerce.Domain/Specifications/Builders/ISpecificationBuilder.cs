namespace ECommerce.Domain.Specifications.Builders;

public interface ISpecificationBuilder<T> where T : class
{
    BaseSpecification<T> Specification { get; }
}

public interface IIncludableSpecificationBuilder<T, out TProperty> : ISpecificationBuilder<T> where T : class
{
}
