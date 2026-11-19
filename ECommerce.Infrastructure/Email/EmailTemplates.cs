using System.Net;

namespace ECommerce.Infrastructure.Email;

/// <summary>
/// HTML email templates. All dynamic values are HTML-encoded at the point of
/// rendering so user-controlled names or URLs cannot inject markup.
/// </summary>
public static class EmailTemplates
{
    public const string PasswordResetSubject = "Password Reset Request";

    public static string RenderPasswordReset(string recipientName, string resetUrl)
    {
        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(recipientName) ? "there" : recipientName);
        var safeUrl = WebUtility.HtmlEncode(resetUrl);

        return $$"""
                <!doctype html>
                <html lang="en">
                  <head>
                    <meta charset="utf-8" />
                    <title>{{PasswordResetSubject}}</title>
                  </head>
                  <body style="margin:0; padding:24px; background:#f5f7fa; font-family:Segoe UI,Arial,sans-serif; color:#1f2937;">
                    <table role="presentation" style="max-width:600px; margin:0 auto; background:#ffffff; border-radius:8px; padding:32px;">
                      <tr><td>
                        <h2 style="margin-top:0;">Password Reset Request</h2>
                        <p>Hello {{safeName}},</p>
                        <p>We received a request to reset your password. Use the button below to choose a new one.</p>
                        <p style="margin:28px 0;">
                          <a href="{{safeUrl}}" style="padding:12px 20px; background:#007bff; color:#ffffff; text-decoration:none; border-radius:4px; display:inline-block;">Reset Password</a>
                        </p>
                        <p>If the button does not work, copy this link into your browser:</p>
                        <p style="word-break:break-all;"><a href="{{safeUrl}}">{{safeUrl}}</a></p>
                        <p>If you did not request this, you can safely ignore this email.</p>
                        <p>This link is time-limited and can only be used once.</p>
                      </td></tr>
                    </table>
                  </body>
                </html>
                """;
    }
}
