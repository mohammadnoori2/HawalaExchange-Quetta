using System.Net;
using System.Net.Mail;
using System.Text;
using HawalaExchange.Application.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Components.Account;

internal sealed class SmtpPlatformMessageSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpPlatformMessageSender> logger) : IPlatformMessageSender
{
    private readonly SmtpEmailOptions settings = options.Value;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Host) && !string.IsNullOrWhiteSpace(settings.FromAddress) && settings.Port is > 0 and <= 65535;
    public async Task SendEmailAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("تنظیمات SMTP تکمیل نشده است.");
        using var message = new MailMessage { From = new MailAddress(settings.FromAddress, settings.FromName, Encoding.UTF8), Subject = subject, SubjectEncoding = Encoding.UTF8, Body = htmlBody, BodyEncoding = Encoding.UTF8, IsBodyHtml = true };
        message.To.Add(recipient);
        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false };
        if (!string.IsNullOrWhiteSpace(settings.UserName)) client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation("SaaS notification email sent to {Recipient}.", recipient);
    }
}
