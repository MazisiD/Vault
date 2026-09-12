namespace VaultID.Services.Configuration;

/// <summary>SMTP coordinates used by <see cref="Remote.SmtpEmailSender"/>.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "VaultID <no-reply@vaultid.example>";
    public bool EnableSsl { get; set; } = true;
}
