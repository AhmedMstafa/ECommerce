using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entityies
{
    public class BaseEntity
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; protected set; }

        public bool IsDeleted { get; protected set; }
        public DateTimeOffset? DeletedAt { get; protected set; }

        //todo created by updated by 

        public Result MarkAsDeleted()
        {
            if (IsDeleted)
                return Result.Failure(Error.Conflict("Entity.AlreadyDeleted", "The entity has already been marked as deleted."));

            IsDeleted = true;
            DeletedAt = DateTimeOffset.UtcNow;
            return Result.Success();
        }
    }
}
