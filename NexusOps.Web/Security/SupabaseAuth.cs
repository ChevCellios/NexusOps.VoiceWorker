using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NexusOps.Web.Security;

public sealed class SupabaseAuthOptions
{
    public const string SectionName = "SupabaseAuth";
    public bool Enabled { get; init; }
    public bool RequireAuthenticatedUsers { get; init; }
    public string Url { get; init; } = string.Empty;
    public string PublishableKey { get; init; } = string.Empty;
}

public enum NexusOpsRole { Viewer, Technician, Manager, Administrator }

public interface IUserRoleStore
{
    Task<NexusOpsRole?> FindRoleAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class PostgresUserRoleStore(NpgsqlDataSource dataSource, Guid tenantId) : IUserRoleStore
{
    public async Task<NexusOpsRole?> FindRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select role from nexusops_user_roles where tenant_id=$1 and user_id=$2 and is_active=true limit 1");
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(userId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string role && Enum.TryParse<NexusOpsRole>(role, true, out var parsed) ? parsed : null;
    }
}

public sealed class UnconfiguredUserRoleStore : IUserRoleStore
{
    public Task<NexusOpsRole?> FindRoleAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<NexusOpsRole?>(null);
}

public sealed record SupabaseSignInResult(bool Succeeded, string? Error, Guid? UserId = null, string? Email = null, NexusOpsRole? Role = null);

public sealed class SupabaseSignInService(HttpClient httpClient, IOptions<SupabaseAuthOptions> options, IUserRoleStore userRoleStore)
{
    public async Task<SupabaseSignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Url) || string.IsNullOrWhiteSpace(settings.PublishableKey))
            return new(false, "Prijava još nije konfigurirana.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Url.TrimEnd('/')}/auth/v1/token?grant_type=password")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("apikey", settings.PublishableKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = error.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase)
                ? "E-mail još nije potvrđen u Supabaseu. Potvrdi korisnika ili ponovno postavi lozinku."
                : error.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase)
                    ? "Supabase publishable/anon ključ nije prihvaćen. Provjeri Railway varijablu."
                    : "E-mail ili lozinka nisu ispravni.";
            return new(false, message);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var user = document.RootElement.GetProperty("user");
        if (!Guid.TryParse(user.GetProperty("id").GetString(), out var userId)) return new(false, "Supabase nije vratio valjani korisnički račun.");

        var role = await userRoleStore.FindRoleAsync(userId, cancellationToken);
        if (role is null) return new(false, "Prijava je uspješna, ali tvoj račun još nema NexusOps ovlast.");
        return new(true, null, userId, user.TryGetProperty("email", out var value) ? value.GetString() : email, role);
    }
}
