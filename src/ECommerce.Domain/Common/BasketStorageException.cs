namespace ECommerce.Domain.Common
{
    /// <summary>
    /// Signals that the primary basket store could not be reached. Unlike the
    /// query cache, a basket failure must never degrade to an empty basket.
    /// </summary>
    public sealed class BasketStorageException : Exception
    {
        public BasketStorageException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
