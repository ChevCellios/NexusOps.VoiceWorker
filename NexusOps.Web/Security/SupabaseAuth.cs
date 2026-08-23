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

public enum NexusOpsRole { Demo, Viewer, Technician, Manager, Administrator }

public interface IUserRoleStore
{
    Task<NexusOpsRole?> FindRoleAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NexusOpsUserAccess>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Guid userId, string email, NexusOpsRole role, CancellationToken cancellationToken);
    Task UpdateAsync(Guid userId, NexusOpsRole role, bool isActive, CancellationToken cancellationToken);
}

public sealed record NexusOpsUserAccess(Guid UserId, string? Email, NexusOpsRole Role, bool IsActive);

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
    public async Task<IReadOnlyList<NexusOpsUserAccess>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select user_id, email, role, is_active from nexusops_user_roles where tenant_id=$1 order by created_at desc"); command.Parameters.AddWithValue(tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var users = new List<NexusOpsUserAccess>();
        while (await reader.ReadAsync(cancellationToken)) users.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1), Enum.Parse<NexusOpsRole>(reader.GetString(2), true), reader.GetBoolean(3)));
        return users;
    }
    public async Task AddAsync(Guid userId, string email, NexusOpsRole role, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("insert into nexusops_user_roles (tenant_id, user_id, email, role) values ($1,$2,$3,$4)");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(email); command.Parameters.AddWithValue(role.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task UpdateAsync(Guid userId, NexusOpsRole role, bool isActive, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("update nexusops_user_roles set role=$3, is_active=$4, updated_at=now() where tenant_id=$1 and user_id=$2");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(userId); command.Parameters.AddWithValue(role.ToString()); command.Parameters.AddWithValue(isActive);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class UnconfiguredUserRoleStore : IUserRoleStore
{
    public Task<NexusOpsRole?> FindRoleAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<NexusOpsRole?>(null);
    public Task<IReadOnlyList<NexusOpsUserAccess>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NexusOpsUserAccess>>([]);
    public Task AddAsync(Guid userId, string email, NexusOpsRole role, CancellationToken cancellationToken) => throw new InvalidOperationException("Baza korisnika nije konfigurirana.");
    public Task UpdateAsync(Guid userId, NexusOpsRole role, bool isActive, CancellationToken cancellationToken) => throw new InvalidOperationException("Baza korisnika nije konfigurirana.");
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

    public async Task<SupabaseSignInResult> CreateUserAsync(string email, string password, NexusOpsRole role, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Url) || string.IsNullOrWhiteSpace(settings.PublishableKey)) return new(false, "Supabase prijava nije konfigurirana.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Url.TrimEnd('/')}/auth/v1/signup") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("apikey", settings.PublishableKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return new(false, "Supabase nije mogao stvoriti korisnika. Provjeri e-mail ili pravila prijave.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var user = document.RootElement.GetProperty("user");
        if (!Guid.TryParse(user.GetProperty("id").GetString(), out var userId)) return new(false, "Nije vraćen UUID novog korisnika.");
        await userRoleStore.AddAsync(userId, email, role, cancellationToken);
        return new(true, null, userId, email, role);
    }
}
