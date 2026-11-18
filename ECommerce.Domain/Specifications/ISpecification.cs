using System.Linq.Expressions;

namespace ECommerce.Domain.Specifications;

public interface ISpecification<T> where T : class
{
    Expression<Func<T, bool>>? Criteria { get; }

    IReadOnlyList<Expression<Func<T, bool>>> WhereExpressions { get; }

    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }

    IReadOnlyList<string> IncludeStrings { get; }

    IReadOnlyList<IncludeExpressionInfo> IncludeExpressions { get; }

    Expression<Func<T, object>>? OrderBy { get; }

    Expression<Func<T, object>>? OrderByDescending { get; }

    IReadOnlyList<OrderExpressionInfo<T>> OrderExpressions { get; }

    int Skip { get; }

    int Take { get; }

    bool IsPagingEnabled { get; }

    bool IsTrackingEnabled { get; }
}

public interface ISpecification<T, TResult> : ISpecification<T> where T : class
{
    Expression<Func<T, TResult>>? Selector { get; }
}
