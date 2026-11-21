using System.Collections.ObjectModel;

namespace ECommerce.Domain.Entities
{
    /// <summary>
    /// Temporary shopping state stored in Redis. It owns quantity and merge
    /// invariants, but never decides stock or price: those stay in SQL.
    /// </summary>
    public sealed class Basket
    {
        // Only the most recent guest baskets are remembered so a retried merge
        // stays idempotent without letting the marker list grow forever.
        private const int MaximumMergedMarkers = 10;

        private readonly List<BasketItem> _items = [];
        private readonly List<string> _mergedGuestTokenHashes = [];

        private Basket() { }

        private Basket(
            Guid id,
            Guid? userId,
            string? guestTokenHash,
            DateTime createdAt,
            DateTime updatedAt,
            long revision,
            long expectedRevision)
        {
            Id = id;
            UserId = userId;
            GuestTokenHash = guestTokenHash;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            Revision = revision;
            ExpectedRevision = expectedRevision;
        }

        public Guid Id { get; private set; }

        public Guid? UserId { get; private set; }

        public string? GuestTokenHash { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime UpdatedAt { get; private set; }

        public long Revision { get; private set; }

        /// <summary>
        /// Revision value expected in the backing store for optimistic concurrency.
        /// -1 means the basket has not been persisted yet.
        /// </summary>
        public long ExpectedRevision { get; private set; }

        public ReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();

        public ReadOnlyCollection<string> MergedGuestTokenHashes => _mergedGuestTokenHashes.AsReadOnly();

        public static Basket CreateGuest(string guestTokenHash, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(guestTokenHash))
            {
                throw new ArgumentException("A guest token hash is required.", nameof(guestTokenHash));
            }

            return new Basket(Guid.NewGuid(), null, guestTokenHash, utcNow, utcNow, 0, -1);
        }

        public static Basket CreateUser(Guid userId, DateTime utcNow)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("A valid user id is required.", nameof(userId));
            }

            return new Basket(Guid.NewGuid(), userId, null, utcNow, utcNow, 0, -1);
        }

        public static Basket Rehydrate(
            Guid id,
            Guid? userId,
            string? guestTokenHash,
            DateTime createdAt,
            DateTime updatedAt,
            long revision,
            IEnumerable<BasketItem> items,
            IEnumerable<string> mergedGuestTokenHashes)
        {
            var basket = new Basket(id, userId, guestTokenHash, createdAt, updatedAt, revision, revision);
            basket._items.AddRange(items);
            basket._mergedGuestTokenHashes.AddRange(mergedGuestTokenHashes);
            return basket;
        }

        public void AddOrIncrease(
            Guid productId,
            int quantity,
            decimal? priceAtAdd,
            DateTime utcNow)
        {
            var existing = Find(productId);
            if (existing is null)
            {
                _items.Add(BasketItem.Create(productId, quantity, priceAtAdd, utcNow));
            }
            else
            {
                // Keep the first snapshot so a later price change is visible
                // instead of being silently overwritten by a repeated add.
                existing.IncreaseQuantity(quantity);
            }

            Touch(utcNow);
        }

        public bool Remove(Guid productId, DateTime utcNow)
        {
            var existing = Find(productId);
            if (existing is null)
            {
                return false;
            }

            _items.Remove(existing);
            Touch(utcNow);
            return true;
        }

        public bool SetQuantity(
            Guid productId,
            int quantity,
            decimal? priceAtAdd,
            DateTime utcNow)
        {
            var existing = Find(productId);
            if (existing is null)
            {
                return false;
            }

            existing.SetQuantity(quantity);
            existing.SetPriceAtAddIfMissing(priceAtAdd);

            Touch(utcNow);
            return true;
        }

        public void Clear(DateTime utcNow)
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            Touch(utcNow);
        }

        public bool HasMergedGuest(string guestTokenHash) =>
            _mergedGuestTokenHashes.Contains(guestTokenHash, StringComparer.Ordinal);

        public void MarkGuestMerged(string guestTokenHash, DateTime utcNow)
        {
            if (HasMergedGuest(guestTokenHash))
            {
                return;
            }

            _mergedGuestTokenHashes.Add(guestTokenHash);

            while (_mergedGuestTokenHashes.Count > MaximumMergedMarkers)
            {
                _mergedGuestTokenHashes.RemoveAt(0);
            }

            Touch(utcNow);
        }

        public void Touch(DateTime utcNow)
        {
            UpdatedAt = utcNow;
            Revision++;
        }

        public void MarkPersisted()
        {
            ExpectedRevision = Revision;
        }

        private BasketItem? Find(Guid productId) =>
            _items.FirstOrDefault(item => item.ProductId == productId);
    }
}
