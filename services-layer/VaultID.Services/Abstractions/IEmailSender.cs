namespace VaultID.Services.Abstractions;

/// <summary>Outbound email delivery - an external I/O concern, alongside the webhook delivery this layer is meant to grow into.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
}
