using VaultID.Services.Abstractions;

namespace VaultID.Application.Services;

/// <summary>
/// "Forgot username" business logic (blueprint: account recovery). Looks up
/// the username registered against an email and, if one exists, emails it.
/// Deliberately never reveals to the caller whether a match was found, so
/// this can't be used to enumerate which emails have an account.
/// </summary>
public sealed class AccountRecoveryService(IAccountDirectoryStore directory, IEmailSender email)
{
    public async Task SendUsernameReminderAsync(string emailAddress, CancellationToken ct = default)
    {
        var username = await directory.GetUsernameForEmailAsync(emailAddress, ct);
        if (username is null)
        {
            return;
        }

        await email.SendAsync(
            emailAddress,
            "Your VaultID username",
            $"Hi,\n\nThe username for this VaultID account is: {username}\n\n" +
            "If you didn't request this, you can safely ignore this email.",
            ct);
    }
}
