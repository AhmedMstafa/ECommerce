using ECommerce.Domain.Entities;

namespace ECommerce.UseCases.Common.Baskets;

/// <summary>
/// Basket limits and Redis TTLs. Bound from the "Basket" configuration section
/// with safe defaults so a missing section does not break startup.
/// </summary>
public sealed class BasketSettings
{
    public const string SectionName = "Basket";

    public int AnonymousExpirationDays { get; set; } = 7;

    public int AuthenticatedExpirationDays { get; set; } = 30;

    /// <summary>
    /// Hard ceiling measured from basket creation. Reaching it starts a fresh
    /// basket rather than keeping old state alive through activity.
    /// </summary>
    public int MaximumLifetimeDays { get; set; } = 60;

    public int MaximumItems { get; set; } = 50;

    public int MaximumQuantityPerProduct { get; set; } = 20;

    public void Validate()
    {
        if (AnonymousExpirationDays <= 0 || AuthenticatedExpirationDays <= 0 || MaximumLifetimeDays <= 0)
        {
            throw new InvalidOperationException(
                "Basket expiration values must be positive days.");
        }

        if (MaximumLifetimeDays < Math.Max(AnonymousExpirationDays, AuthenticatedExpirationDays))
        {
            throw new InvalidOperationException(
                "Basket:MaximumLifetimeDays must be at least the largest sliding expiration.");
        }

        if (MaximumItems <= 0 || MaximumQuantityPerProduct <= 0)
        {
            throw new InvalidOperationException(
                "Basket:MaximumItems and Basket:MaximumQuantityPerProduct must be positive.");
        }
    }

    public TimeSpan SlidingExpiration(BasketKind kind) => TimeSpan.FromDays(
        kind == BasketKind.User ? AuthenticatedExpirationDays : AnonymousExpirationDays);

    public TimeSpan MaximumLifetime => TimeSpan.FromDays(MaximumLifetimeDays);
}
