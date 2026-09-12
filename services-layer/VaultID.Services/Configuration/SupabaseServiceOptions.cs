namespace VaultID.Services.Configuration;

/// <summary>Supabase project coordinates for server-to-server calls that need the service_role key.</summary>
public sealed class SupabaseServiceOptions
{
    public string Url { get; set; } = string.Empty;

    /// <summary>Bypasses Row Level Security - server-side only, never expose this to a client.</summary>
    public string ServiceRoleKey { get; set; } = string.Empty;
}
