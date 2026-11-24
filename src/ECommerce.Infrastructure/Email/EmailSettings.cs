namespace ECommerce.Infrastructure.Email;

public class EmailSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string SenderName { get; set; } = "ECommerce Support";
    public string SenderEmail { get; set; } = string.Empty;
}
