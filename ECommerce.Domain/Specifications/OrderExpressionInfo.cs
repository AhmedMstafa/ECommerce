using System.Linq.Expressions;

namespace ECommerce.Domain.Specifications;

public enum OrderType
{
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending
}

public record OrderExpressionInfo<T>(Expression<Func<T, object>> KeySelector, OrderType OrderType);
