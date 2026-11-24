using System.Linq.Expressions;

namespace ECommerce.Domain.Specifications;

public enum IncludeType
{
    Include = 1,
    ThenInclude = 2
}

public record IncludeExpressionInfo(
    LambdaExpression Expression,
    Type EntityType,
    Type PropertyType,
    Type? PreviousPropertyType = null,
    IncludeType Type = IncludeType.Include);
