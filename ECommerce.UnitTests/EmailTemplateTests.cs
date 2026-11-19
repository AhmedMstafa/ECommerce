using ECommerce.Infrastructure.Email;
using FluentAssertions;

namespace ECommerce.UnitTests;

public class EmailTemplateTests
{
    [Fact]
    public void RenderPasswordReset_ShouldHtmlEncodeDynamicValues()
    {
        var html = EmailTemplates.RenderPasswordReset(
            "<script>alert(1)</script>",
            "https://client.example.com/reset?email=a+b@example.com&token=x<y");

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("a+b@example.com&amp;token=x&lt;y");
        html.Should().Contain("Reset Password");
    }

    [Fact]
    public void RenderPasswordReset_ShouldUseFriendlyFallbackName()
    {
        var html = EmailTemplates.RenderPasswordReset("   ", "https://client.example.com/reset");

        html.Should().Contain("Hello there,");
    }
}
