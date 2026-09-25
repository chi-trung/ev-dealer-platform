using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace UserService.Services;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration cfg, ILogger<EmailService> logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
    {
        var emailSettings = _cfg.GetSection("EmailSettings");
        var smtpHost = emailSettings.GetValue<string>("SmtpHost");
        var smtpPort = emailSettings.GetValue<int>("SmtpPort");
        var smtpUser = emailSettings.GetValue<string>("SmtpUser");
        var smtpPassword = emailSettings.GetValue<string>("SmtpPassword");
        var fromEmail = emailSettings.GetValue<string>("FromEmail") ?? smtpUser;
        var fromName = emailSettings.GetValue<string>("FromName") ?? "EV Dealer Management";
        var enableSsl = emailSettings.GetValue<bool>("EnableSsl", true);

        // If SMTP not configured, log instead of sending
        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser))
        {
            Console.WriteLine("\n=== PASSWORD RESET EMAIL ===");
            Console.WriteLine($"To: {toEmail}");
            Console.WriteLine($"Subject: Password Reset Request");
            Console.WriteLine($"Reset Link: {resetLink}");
            Console.WriteLine("============================\n");
            _logger.LogInformation("SMTP not configured. Password reset link logged to console for {Email}", toEmail);
            return;
        }

        try
        {
            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(smtpUser, smtpPassword);

            var message = new MimeKit.MimeMessage();
            message.From.Add(new MimeKit.MailboxAddress(fromName, fromEmail));
            message.To.Add(new MimeKit.MailboxAddress(userName, toEmail));
            message.Subject = "Reset Your Password - EV Dealer Management";

            var bodyBuilder = new MimeKit.BodyBuilder
            {
                HtmlBody = $@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <style>
                            body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                            .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                            .header {{ background-color: #4CAF50; color: white; padding: 20px; text-align: center; }}
                            .content {{ background-color: #f9f9f9; padding: 30px; border-radius: 5px; margin-top: 20px; }}
                            .button {{ display: inline-block; padding: 12px 30px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
                            .footer {{ text-align: center; margin-top: 30px; font-size: 12px; color: #666; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h1>Password Reset Request</h1>
                            </div>
                            <div class='content'>
                                <p>Hello {userName},</p>
                                <p>We received a request to reset your password for your EV Dealer Management account.</p>
                                <p>Click the button below to reset your password:</p>
                                <p style='text-align: center;'>
                                    <a href='{resetLink}' class='button'>Reset Password</a>
                                </p>
                                <p>Or copy and paste this link into your browser:</p>
                                <p style='word-break: break-all; color: #666;'>{resetLink}</p>
                                <p><strong>This link will expire in 1 hour.</strong></p>
                                <p>If you didn't request a password reset, please ignore this email or contact support if you have concerns.</p>
                            </div>
                            <div class='footer'>
                                <p>&copy; 2024 EV Dealer Management System. All rights reserved.</p>
                            </div>
                        </div>
                    </body>
                    </html>
                ",
                TextBody = $@"
Hello {userName},

We received a request to reset your password for your EV Dealer Management account.

Click the link below to reset your password:
{resetLink}

This link will expire in 1 hour.

If you didn't request a password reset, please ignore this email or contact support if you have concerns.

© 2024 EV Dealer Management System. All rights reserved.
                "
            };

            message.Body = bodyBuilder.ToMessageBody();

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw new Exception("Failed to send email. Please try again later.");
        }
    }
}
