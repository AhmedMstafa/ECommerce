namespace ECommerce.Infrastructure.Auth;

public class JwtSettings
{
    public string Issuer { get; set; } = "ECommerceApi";
    public string Audience { get; set; } = "ECommerceClient";
    public string SecretKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 120;
}
