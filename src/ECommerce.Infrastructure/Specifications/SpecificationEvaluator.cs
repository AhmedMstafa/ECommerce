using System.Reflection;
using ECommerce.Domain.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace ECommerce.Infrastructure.Specifications
{
    public static class SpecificationEvaluator
    {
        private static readonly MethodInfo IncludeMethodInfo = typeof(EntityFrameworkQueryableExtensions)
            .GetTypeInfo().GetDeclaredMethods(nameof(EntityFrameworkQueryableExtensions.Include))
            .Single(mi => mi.GetGenericArguments().Length == 2
                && mi.GetParameters().Length == 2
                && mi.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(System.Linq.Expressions.Expression<>));

        private static readonly MethodInfo ThenIncludeAfterReferenceMethodInfo = typeof(EntityFrameworkQueryableExtensions)
            .GetTypeInfo().GetDeclaredMethods(nameof(EntityFrameworkQueryableExtensions.ThenInclude))
            .Single(mi => mi.GetGenericArguments().Length == 3
                && mi.GetParameters()[0].ParameterType.GenericTypeArguments[1].IsGenericMethodParameter);

        private static readonly MethodInfo ThenIncludeAfterCollectionMethodInfo = typeof(EntityFrameworkQueryableExtensions)
            .GetTypeInfo().GetDeclaredMethods(nameof(EntityFrameworkQueryableExtensions.ThenInclude))
            .Single(mi => mi.GetGenericArguments().Length == 3
                && mi.GetParameters()[0].ParameterType.GenericTypeArguments[1].IsGenericType
                && mi.GetParameters()[0].ParameterType.GenericTypeArguments[1].GetGenericTypeDefinition() == typeof(IEnumerable<>));

        public static IQueryable<T> GetQuery<T>(
            IQueryable<T> inputQuery,
            ISpecification<T> specification) where T : class
        {
            var query = inputQuery;

            if (!specification.IsTrackingEnabled)
            {
                query = query.AsNoTracking();
            }

            if (specification.Criteria is not null)
            {
                query = query.Where(specification.Criteria);
            }

            foreach (var whereExpr in specification.WhereExpressions)
            {
                query = query.Where(whereExpr);
            }

            // Apply direct Expression includes
            query = specification.Includes.Aggregate(
                query,
                (current, include) => current.Include(include));

            // Apply string includes
            query = specification.IncludeStrings.Aggregate(
                query,
                (current, include) => current.Include(include));

            // Apply IncludeExpressions (Include & ThenInclude chained via SpecificationBuilder)
            if (specification.IncludeExpressions.Count > 0 && IsEntityFrameworkQueryable(query))
            {
                query = ApplyIncludeExpressions(query, specification.IncludeExpressions);
            }

            if (specification.OrderExpressions.Count > 0)
            {
                IOrderedQueryable<T>? orderedQuery = null;
                foreach (var order in specification.OrderExpressions)
                {
                    if (orderedQuery is null)
                    {
                        orderedQuery = order.OrderType switch
                        {
                            OrderType.OrderBy => query.OrderBy(order.KeySelector),
                            OrderType.OrderByDescending => query.OrderByDescending(order.KeySelector),
                            _ => query.OrderBy(order.KeySelector)
                        };
                    }
                    else
                    {
                        orderedQuery = order.OrderType switch
                        {
                            OrderType.ThenBy or OrderType.OrderBy => orderedQuery.ThenBy(order.KeySelector),
                            OrderType.ThenByDescending or OrderType.OrderByDescending => orderedQuery.ThenByDescending(order.KeySelector),
                            _ => orderedQuery.ThenBy(order.KeySelector)
                        };
                    }
                }

                if (orderedQuery is not null)
                {
                    query = orderedQuery;
                }
            }
            else if (specification.OrderBy is not null)
            {
                query = query.OrderBy(specification.OrderBy);
            }
            else if (specification.OrderByDescending is not null)
            {
                query = query.OrderByDescending(specification.OrderByDescending);
            }

            if (specification.IsPagingEnabled)
            {
                query = query.Skip(specification.Skip).Take(specification.Take);
            }

            return query;
        }

        public static IQueryable<TResult> GetQuery<T, TResult>(
            IQueryable<T> inputQuery,
            ISpecification<T, TResult> specification) where T : class
        {
            var query = GetQuery(inputQuery, (ISpecification<T>)specification);

            if (specification.Selector is not null)
            {
                return query.Select(specification.Selector);
            }

            throw new InvalidOperationException("Selector expression must be provided when using ISpecification<T, TResult>.");
        }

        private static IQueryable<T> ApplyIncludeExpressions<T>(
            IQueryable<T> query,
            IReadOnlyList<IncludeExpressionInfo> includeExpressions) where T : class
        {
            object currentQuery = query;

            foreach (var includeInfo in includeExpressions)
            {
                if (includeInfo.Type == IncludeType.Include)
                {
                    var method = IncludeMethodInfo.MakeGenericMethod(typeof(T), includeInfo.PropertyType);
                    currentQuery = method.Invoke(null, [currentQuery, includeInfo.Expression])!;
                }
                else if (includeInfo.Type == IncludeType.ThenInclude)
                {
                    if (includeInfo.PreviousPropertyType is not null &&
                        typeof(System.Collections.IEnumerable).IsAssignableFrom(includeInfo.PreviousPropertyType) &&
                        includeInfo.PreviousPropertyType.IsGenericType)
                    {
                        var previousElementType = includeInfo.PreviousPropertyType.GenericTypeArguments[0];
                        var method = ThenIncludeAfterCollectionMethodInfo.MakeGenericMethod(typeof(T), previousElementType, includeInfo.PropertyType);
                        currentQuery = method.Invoke(null, [currentQuery, includeInfo.Expression])!;
                    }
                    else
                    {
                        var method = ThenIncludeAfterReferenceMethodInfo.MakeGenericMethod(typeof(T), includeInfo.PreviousPropertyType!, includeInfo.PropertyType);
                        currentQuery = method.Invoke(null, [currentQuery, includeInfo.Expression])!;
                    }
                }
            }

            return (IQueryable<T>)currentQuery;
        }

        private static bool IsEntityFrameworkQueryable(IQueryable query)
        {
            var providerType = query.Provider.GetType();
            return providerType.Assembly.GetName().Name?.StartsWith("Microsoft.EntityFrameworkCore") == true;
        }
    }
}
