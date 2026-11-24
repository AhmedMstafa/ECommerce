using ECommerce.Domain.Enums;

namespace ECommerce.Domain.Entities;

public sealed class StockReservation : AuditedEntity
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? FinalizedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public DateTime? RestockedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = default!;

    private StockReservation() { }

    public StockReservation(Guid orderId, Guid productId, int quantity, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        Id = Guid.NewGuid();
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        Status = ReservationStatus.Reserved;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = new DateTimeOffset(createdAtUtc, TimeSpan.Zero);
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsActive => Status == ReservationStatus.Reserved && ExpiresAtUtc > DateTime.UtcNow;

    public void Finalize(DateTime utcNow)
    {
        if (Status != ReservationStatus.Reserved)
            throw new InvalidOperationException("Only reserved reservations can be finalized.");

        Status = ReservationStatus.Finalized;
        FinalizedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public void Release(DateTime utcNow, bool expired = false)
    {
        if (Status != ReservationStatus.Reserved)
            throw new InvalidOperationException("Only reserved reservations can be released.");

        Status = expired ? ReservationStatus.Expired : ReservationStatus.Released;
        ReleasedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    public void MarkRestocked(DateTime utcNow)
    {
        if (Status != ReservationStatus.Finalized)
            throw new InvalidOperationException("Only finalized reservations can be restocked.");
        if (RestockedAtUtc is not null)
            throw new InvalidOperationException("Reservation was already restocked.");

        RestockedAtUtc = utcNow;
        UpdatedAtUtc = new DateTimeOffset(utcNow, TimeSpan.Zero);
    }
}
