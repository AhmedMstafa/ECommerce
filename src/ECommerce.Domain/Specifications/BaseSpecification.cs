using System.Linq.Expressions;
using ECommerce.Domain.Specifications.Builders;

namespace ECommerce.Domain.Specifications;

public abstract class BaseSpecification<T> : ISpecification<T> where T : class
{
    protected BaseSpecification()
    {
    }

    protected BaseSpecification(Expression<Func<T, bool>> criteria)
    {
        Criteria = criteria;
    }

    /// <summary>
    /// Fluent builder for constructing specifications cleanly (e.g. Query.Where(...).Include(...).ThenInclude(...)).
    /// </summary>
    public ISpecificationBuilder<T> Query => new SpecificationBuilder<T>(this);

    public Expression<Func<T, bool>>? Criteria { get; private set; }

    private readonly List<Expression<Func<T, bool>>> _whereExpressions = [];
    public IReadOnlyList<Expression<Func<T, bool>>> WhereExpressions => _whereExpressions;

    private readonly List<Expression<Func<T, object>>> _includes = [];
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _includes;

    private readonly List<string> _includeStrings = [];
    public IReadOnlyList<string> IncludeStrings => _includeStrings;

    private readonly List<IncludeExpressionInfo> _includeExpressions = [];
    public IReadOnlyList<IncludeExpressionInfo> IncludeExpressions => _includeExpressions;

    public Expression<Func<T, object>>? OrderBy { get; private set; }

    public Expression<Func<T, object>>? OrderByDescending { get; private set; }

    private readonly List<OrderExpressionInfo<T>> _orderExpressions = [];
    public IReadOnlyList<OrderExpressionInfo<T>> OrderExpressions => _orderExpressions;

    public int Skip { get; private set; }

    public int Take { get; private set; }

    public bool IsPagingEnabled { get; private set; }

    public bool IsTrackingEnabled { get; private set; }

    public void AddCriteria(Expression<Func<T, bool>> criteria)
    {
        Criteria = criteria;
    }

    public void AddWhere(Expression<Func<T, bool>> whereExpression)
    {
        _whereExpressions.Add(whereExpression);
    }

    public void AddInclude(Expression<Func<T, object>> includeExpression)
    {
        _includes.Add(includeExpression);
    }

    public void AddInclude(string includeString)
    {
        _includeStrings.Add(includeString);
    }

    public void AddIncludeExpression(IncludeExpressionInfo includeInfo)
    {
        _includeExpressions.Add(includeInfo);
    }

    public void AddOrderBy(Expression<Func<T, object>> orderByExpression)
    {
        OrderBy = orderByExpression;
        _orderExpressions.Add(new OrderExpressionInfo<T>(orderByExpression, OrderType.OrderBy));
    }

    public void AddOrderByDescending(Expression<Func<T, object>> orderByDescExpression)
    {
        OrderByDescending = orderByDescExpression;
        _orderExpressions.Add(new OrderExpressionInfo<T>(orderByDescExpression, OrderType.OrderByDescending));
    }

    public void AddThenBy(Expression<Func<T, object>> thenByExpression)
    {
        _orderExpressions.Add(new OrderExpressionInfo<T>(thenByExpression, OrderType.ThenBy));
    }

    public void AddThenByDescending(Expression<Func<T, object>> thenByDescExpression)
    {
        _orderExpressions.Add(new OrderExpressionInfo<T>(thenByDescExpression, OrderType.ThenByDescending));
    }

    public void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }

    public void EnableTracking()
    {
        IsTrackingEnabled = true;
    }

    public void DisableTracking()
    {
        IsTrackingEnabled = false;
    }
}

public abstract class BaseSpecification<T, TResult> : BaseSpecification<T>, ISpecification<T, TResult> where T : class
{
    protected BaseSpecification()
    {
    }

    protected BaseSpecification(Expression<Func<T, bool>> criteria) : base(criteria)
    {
    }

    public Expression<Func<T, TResult>>? Selector { get; private set; }

    public void AddSelector(Expression<Func<T, TResult>> selector)
    {
        Selector = selector;
    }
}
