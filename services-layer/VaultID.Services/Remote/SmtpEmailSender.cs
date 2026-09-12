using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VaultID.Services.Abstractions;
using VaultID.Services.Configuration;

namespace VaultID.Services.Remote;

/// <summary>
/// Sends plain-text emails via SMTP. If no host is configured (e.g. local dev
/// without SMTP set up), the email is logged instead of sent so callers still
/// complete successfully.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            logger.LogWarning(
                "Smtp:Host is not configured - skipping send. Would have emailed {ToEmail}: {Subject}\n{Body}",
                toEmail, subject, body);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        var socketOptions = _options.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);
        if (!string.IsNullOrEmpty(_options.User))
        {
            await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);
        }
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
