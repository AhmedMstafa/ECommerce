namespace ECommerce.Domain.Entities;

public abstract class AuditedEntity
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAtUtc { get; protected internal set; }
    public DateTimeOffset UpdatedAtUtc { get; protected internal set; }
}
