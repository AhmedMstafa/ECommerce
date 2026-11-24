namespace ECommerce.Domain.Entities
{
    /// <summary>
    /// Logical basket identity. The guest identifier is already a one-way token
    /// hash; the raw guest token never reaches the storage layer.
    /// </summary>
    public enum BasketKind
    {
        Guest,
        User
    }

    public sealed record BasketKey
    {
        private BasketKey(BasketKind kind, string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException("A basket identifier is required.", nameof(identifier));
            }

            Kind = kind;
            Identifier = identifier;
        }

        public BasketKind Kind { get; }

        public string Identifier { get; }

        public string Value => Kind == BasketKind.User
            ? $"ECommerce:Basket:User:{Identifier}"
            : $"ECommerce:Basket:Guest:{Identifier}";

        public static BasketKey ForUser(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("A valid user id is required.", nameof(userId));
            }

            return new BasketKey(BasketKind.User, userId.ToString("D"));
        }

        public static BasketKey ForGuest(string guestTokenHash) =>
            new(BasketKind.Guest, guestTokenHash);

        public override string ToString() => Value;
    }
}
