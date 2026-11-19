namespace ECommerce.Infrastructure.Email;

public class EmailSettings
{
    public string SmtpServer { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public string SenderName { get; set; } = "ECommerce Support";
    public string SenderEmail { get; set; } = "support@ecommerce.local";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; }
}
