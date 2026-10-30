namespace ECommerce.Domain.Entityies
{
    public class BaseEntity
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; protected set; }
        //todo created by updated by 
    }
}
