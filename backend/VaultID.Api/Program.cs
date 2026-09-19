using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using VaultID.Api.Infrastructure;
using VaultID.Application.DependencyInjection;

// Supabase JWTs use the short claim names ("sub", "email") as-is; without this,
// the JWT handler silently remaps "sub" to the long ClaimTypes.NameIdentifier URI.
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

var builder = WebApplication.CreateBuilder(args);

// --- Services ---
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
        // Serialise/accept enums as their names (e.g. "Biographical"), matching
        // the contract the Angular client uses.
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "VaultID API",
            Version = "v1",
            Description = "Event-sourced identity vault: organisations, consent, sharing and expiry."
        };
        return Task.CompletedTask;
    }));

// Wire the entire backend (Application -> Services). The Api never registers
// data-access or business services directly; it delegates to the composition
// root of the Application layer. Prefer real Postgres when a connection string
// is configured, and fall back to the in-memory stores for local/dev work.
var postgresConnectionString = builder.Configuration["Supabase:ConnectionString"]
    ?? builder.Configuration.GetConnectionString("Supabase")
    ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddVaultIdApplication(postgresConnectionString);
}
else
{
    builder.Services.AddVaultIdApplication();
}

// --- Auth ---
// Registration, login and password reset are handled by the Angular client
// talking to Supabase Auth directly. The backend only needs to validate the
// Supabase-issued JWT on incoming requests here; the "forgot username" flow's
// actual Supabase/SMTP calls are registered via the Application layer's
// composition root below, same as every other data-access concern.
builder.Services.AddVaultIdAccountRecovery(
    options => builder.Configuration.GetSection("Supabase").Bind(options),
    options => builder.Configuration.GetSection("Smtp").Bind(options));

var supabaseOptions = builder.Configuration.GetSection("Supabase").Get<SupabaseOptions>() ?? new SupabaseOptions();
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection("Supabase"));
builder.Services.AddHttpClient<SupabaseJwksProvider>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidIssuer = $"{supabaseOptions.Url.TrimEnd('/')}/auth/v1";
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidAudience = "authenticated";
        options.TokenValidationParameters.ValidateLifetime = true;
    });
// Newer Supabase projects sign session JWTs with an asymmetric key (ES256),
// published at "{Url}/auth/v1/.well-known/jwks.json" - validate against that
// public key set rather than a shared secret. (A project still using the
// older shared-secret signing would need IssuerSigningKey set to a
// SymmetricSecurityKey from Supabase:JwtSecret instead.)
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SupabaseJwksProvider>((options, jwks) =>
    {
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) =>
            jwks.GetSigningKeysAsync().GetAwaiter().GetResult()
                .Where(key => kid is null || key.KeyId == kid);
    });
builder.Services.AddAuthorization();

// CORS so the Angular presentation layer (http://localhost:4200) can call us.
const string AngularCors = "angular-dev";
builder.Services.AddCors(options =>
    options.AddPolicy(AngularCors, policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

// --- Pipeline ---
// Translates Application exceptions (NotFound/Validation/Conflict) into RFC7807
// problem responses. Keeps controllers free of try/catch boilerplate.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "VaultID API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "VaultID API";
        // Collapse operation list by default; easier to scan a large surface.
        options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        options.DisplayRequestDuration();
    });
}

app.UseCors(AngularCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed a couple of demo organisations so the UI has something to share with.
await DemoData.SeedAsync(app.Services);

app.Run();

// Exposes the generated Program class to VaultID.Tests's WebApplicationFactory<Program>.
public partial class Program;
