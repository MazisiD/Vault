using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VaultID.Services.Abstractions;
using VaultID.Services.Configuration;

namespace VaultID.Services.Remote;

/// <summary>
/// Talks to Supabase's PostgREST endpoint using the service_role key (bypasses
/// Row Level Security) to call the `get_username_for_email` function defined
/// in supabase/schema.sql. This is the only place that calls Supabase
/// directly for account recovery - the Application and Api layers depend only
/// on <see cref="IAccountDirectoryStore"/>.
/// </summary>
public sealed class SupabaseAccountDirectoryStore : IAccountDirectoryStore
{
    private readonly HttpClient _http;

    public SupabaseAccountDirectoryStore(HttpClient http, IOptions<SupabaseServiceOptions> options)
    {
        var config = options.Value;
        http.BaseAddress = new Uri(config.Url.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("apikey", config.ServiceRoleKey);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ServiceRoleKey}");
        _http = http;
    }

    public async Task<string?> GetUsernameForEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "rest/v1/rpc/get_username_for_email",
            new { p_email = email },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.String
            ? document.RootElement.GetString()
            : null;
    }
}
