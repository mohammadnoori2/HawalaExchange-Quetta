using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using HawalaExchange.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Components.Account;

internal sealed class SmtpIdentityEmailSender(
    IOptions<SmtpEmailOptions> options,
    ILogger<SmtpIdentityEmailSender> logger) : IEmailSender<ApplicationUser>
{
    private readonly SmtpEmailOptions options = options.Value;

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "تأیید ایمیل",
            BuildMessage("تأیید ایمیل", "برای تأیید ایمیل خود روی دکمه زیر کلیک کنید.", confirmationLink, "تأیید ایمیل"));

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "بازیابی رمز عبور",
            BuildMessage("بازیابی رمز عبور", "برای تعیین رمز عبور جدید روی دکمه زیر کلیک کنید.", resetLink, "تعیین رمز جدید"));

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "کد بازیابی رمز عبور",
            $"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;line-height:1.9'>" +
            $"<h2>کد بازیابی رمز عبور</h2><p>کد بازیابی شما:</p>" +
            $"<p style='font-size:24px;font-weight:700;letter-spacing:3px'>{HtmlEncoder.Default.Encode(resetCode)}</p>" +
            "<p style='color:#64748b'>اگر این درخواست را شما ثبت نکرده‌اید، این پیام را نادیده بگیرید.</p></div>");

    private async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        ValidateConfiguration();

        using var message = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = htmlBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(recipient));

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);

        try
        {
            await client.SendMailAsync(message);
            logger.LogInformation("Password-related email sent to {Recipient}.", recipient);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password-related email to {Recipient}.", recipient);
            throw;
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.FromAddress))
            throw new InvalidOperationException("تنظیمات ارسال ایمیل تکمیل نشده است. Email:Host و Email:FromAddress را تنظیم کنید.");

        if (options.Port is <= 0 or > 65535)
            throw new InvalidOperationException("پورت SMTP معتبر نیست.");
    }

    private static string BuildMessage(string title, string description, string link, string buttonText) =>
        $"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;max-width:560px;margin:auto;line-height:1.9;color:#1e293b'>" +
        $"<h2 style='color:#312e81'>{title}</h2><p>{description}</p>" +
        $"<p style='margin:28px 0'><a href='{link}' style='background:#4f46e5;color:#fff;padding:12px 22px;border-radius:10px;text-decoration:none;font-weight:700'>{buttonText}</a></p>" +
        "<p style='color:#64748b;font-size:13px'>اگر این درخواست را شما ثبت نکرده‌اید، این پیام را نادیده بگیرید.</p></div>";
}
