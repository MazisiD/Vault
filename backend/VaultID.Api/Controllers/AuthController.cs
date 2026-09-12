using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Application.Services;

namespace VaultID.Api.Controllers;

/// <summary>
/// Account-recovery endpoints that Supabase Auth doesn't provide out of the
/// box. Registration, login, and password reset are handled directly by the
/// Angular client against Supabase Auth - this controller only covers
/// "forgot username". Transport only: delegates entirely to
/// <see cref="AccountRecoveryService"/>, same as every other controller.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(AccountRecoveryService recovery) : ControllerBase
{
    [HttpPost("forgot-username")]
    public async Task<IActionResult> ForgotUsername(ForgotUsernameBody body, CancellationToken ct)
    {
        await recovery.SendUsernameReminderAsync(body.Email, ct);

        // Always 200 regardless of whether an account matched - enforced by
        // AccountRecoveryService - so this endpoint can't be used to discover
        // which emails have an account.
        return Ok(new { message = "If that email is registered, we've sent the username to it." });
    }
}
