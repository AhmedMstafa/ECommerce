using System.Linq.Expressions;

namespace ECommerce.Domain.Specifications.Builders;

public static class SpecificationBuilderExtensions
{
    public static ISpecificationBuilder<T> Where<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, bool>> criteria) where T : class
    {
        builder.Specification.AddWhere(criteria);
        return builder;
    }

    public static IIncludableSpecificationBuilder<T, TProperty> Include<T, TProperty>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, TProperty>> includeExpression) where T : class
    {
        var info = new IncludeExpressionInfo(
            includeExpression,
            typeof(T),
            typeof(TProperty),
            null,
            IncludeType.Include);

        builder.Specification.AddIncludeExpression(info);
        return new IncludableSpecificationBuilder<T, TProperty>(builder.Specification);
    }

    public static ISpecificationBuilder<T> Include<T>(
        this ISpecificationBuilder<T> builder,
        string includeString) where T : class
    {
        builder.Specification.AddInclude(includeString);
        return builder;
    }

    /// <summary>
    /// ThenInclude for reference (1-to-1 or Many-to-1) navigation properties.
    /// </summary>
    public static IIncludableSpecificationBuilder<T, TProperty> ThenInclude<T, TPreviousProperty, TProperty>(
        this IIncludableSpecificationBuilder<T, TPreviousProperty> builder,
        Expression<Func<TPreviousProperty, TProperty>> thenIncludeExpression) where T : class
    {
        var info = new IncludeExpressionInfo(
            thenIncludeExpression,
            typeof(T),
            typeof(TProperty),
            typeof(TPreviousProperty),
            IncludeType.ThenInclude);

        builder.Specification.AddIncludeExpression(info);
        return new IncludableSpecificationBuilder<T, TProperty>(builder.Specification);
    }

    /// <summary>
    /// ThenInclude for collection (1-to-Many) navigation properties (e.g. Orders -> OrderItems).
    /// </summary>
    public static IIncludableSpecificationBuilder<T, TProperty> ThenInclude<T, TPreviousProperty, TProperty>(
        this IIncludableSpecificationBuilder<T, IEnumerable<TPreviousProperty>> builder,
        Expression<Func<TPreviousProperty, TProperty>> thenIncludeExpression) where T : class
    {
        var info = new IncludeExpressionInfo(
            thenIncludeExpression,
            typeof(T),
            typeof(TProperty),
            typeof(IEnumerable<TPreviousProperty>),
            IncludeType.ThenInclude);

        builder.Specification.AddIncludeExpression(info);
        return new IncludableSpecificationBuilder<T, TProperty>(builder.Specification);
    }

    public static ISpecificationBuilder<T> OrderBy<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object>> orderByExpression) where T : class
    {
        builder.Specification.AddOrderBy(orderByExpression);
        return builder;
    }

    public static ISpecificationBuilder<T> OrderByDescending<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object>> orderByDescExpression) where T : class
    {
        builder.Specification.AddOrderByDescending(orderByDescExpression);
        return builder;
    }

    public static ISpecificationBuilder<T> ThenBy<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object>> thenByExpression) where T : class
    {
        builder.Specification.AddThenBy(thenByExpression);
        return builder;
    }

    public static ISpecificationBuilder<T> ThenByDescending<T>(
        this ISpecificationBuilder<T> builder,
        Expression<Func<T, object>> thenByDescExpression) where T : class
    {
        builder.Specification.AddThenByDescending(thenByDescExpression);
        return builder;
    }

    public static ISpecificationBuilder<T> Skip<T>(
        this ISpecificationBuilder<T> builder,
        int skip) where T : class
    {
        builder.Specification.ApplyPaging(skip, builder.Specification.Take);
        return builder;
    }

    public static ISpecificationBuilder<T> Take<T>(
        this ISpecificationBuilder<T> builder,
        int take) where T : class
    {
        builder.Specification.ApplyPaging(builder.Specification.Skip, take);
        return builder;
    }

    public static ISpecificationBuilder<T> Paginate<T>(
        this ISpecificationBuilder<T> builder,
        int pageIndex,
        int pageSize) where T : class
    {
        var skip = (pageIndex - 1) * pageSize;
        builder.Specification.ApplyPaging(skip, pageSize);
        return builder;
    }

    public static ISpecificationBuilder<T> AsNoTracking<T>(
        this ISpecificationBuilder<T> builder) where T : class
    {
        builder.Specification.DisableTracking();
        return builder;
    }

    public static ISpecificationBuilder<T> AsTracking<T>(
        this ISpecificationBuilder<T> builder) where T : class
    {
        builder.Specification.EnableTracking();
        return builder;
    }
}
