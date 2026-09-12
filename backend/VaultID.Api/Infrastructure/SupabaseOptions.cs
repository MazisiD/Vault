namespace VaultID.Api.Infrastructure;

/// <summary>
/// Bound from the "Supabase" configuration section, for validating the JWT
/// Supabase Auth issues (a transport/middleware concern that belongs at this
/// layer). The service_role key used for actual Supabase data access lives in
/// <see cref="VaultID.Services.Configuration.SupabaseServiceOptions"/> instead
/// - the Api layer never talks to Supabase's API directly, only the services
/// layer does.
/// </summary>
public sealed class SupabaseOptions
{
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric secret used to sign the JWTs Supabase Auth issues (HS256).
    /// If your project uses the newer asymmetric JWT signing keys instead,
    /// swap the JwtBearer configuration in Program.cs for JWKS/Authority-based
    /// validation against "{Url}/auth/v1" instead of a shared secret.
    /// </summary>
    public string JwtSecret { get; set; } = string.Empty;
}
