namespace ECommerce.API.Filters;

/// <summary>
/// Opts a controller or action out of global MVC action execution logging.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SkipActionLoggingAttribute : Attribute
{
}
