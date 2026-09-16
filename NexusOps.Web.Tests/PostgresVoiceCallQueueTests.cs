using Npgsql;
using NexusOps.VoiceWorker.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexusOps.Web.Tests;

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
            Skip = "Set RUN_POSTGRES_INTEGRATION_TESTS=1 to run Docker-backed PostgreSQL tests.";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
            return;

        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        const string sessionsSql = """
            create role anon;
            create role authenticated;
            create table voice_call_sessions (
                id uuid primary key,
                tenant_id uuid not null,
                agent_task_id uuid not null,
                status varchar(30) not null,
                provider_call_id varchar(100)
            )
            """;
        await using (var command = DataSource.CreateCommand(sessionsSql))
            await command.ExecuteNonQueryAsync();

        var migrationPath = Path.Combine(
            AppContext.BaseDirectory, "Database", "Migrations", "005_voice_call_queue.sql");
        var migrationSql = await File.ReadAllTextAsync(migrationPath);
        await using var migration = DataSource.CreateCommand(migrationSql);
        await migration.ExecuteNonQueryAsync();
    }

    public async Task ResetAsync()
    {
        await using var command = DataSource.CreateCommand(
            "truncate table voice_call_queue, voice_call_sessions cascade");
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is null) return;
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

public sealed class PostgresVoiceCallQueueTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [PostgresIntegrationFact]
    public async Task ClaimAsync_ClaimsDistinctJobsAcrossConcurrentConsumers()
    {
        await fixture.ResetAsync();
        var tenantId = Guid.NewGuid();
        var sessions = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var sessionId in sessions)
            await InsertSessionAsync(sessionId, tenantId, Guid.NewGuid());
        var queue = new PostgresVoiceCallQueue(fixture.DataSource);
        await queue.EnqueuePendingSessionsAsync(tenantId, CancellationToken.None);

        var claims = await Task.WhenAll(
            queue.ClaimAsync(tenantId, TimeSpan.FromMinutes(5), CancellationToken.None),
            queue.ClaimAsync(tenantId, TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.All(claims, Assert.NotNull);
        Assert.Equal(2, claims.Select(call => call!.SessionId).Distinct().Count());
    }

    [PostgresIntegrationFact]
    public async Task ClaimAsync_DoesNotCrossTenantBoundary()
    {
        await fixture.ResetAsync();
        var requestedTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var requestedSession = Guid.NewGuid();
        await InsertSessionAsync(requestedSession, requestedTenant, Guid.NewGuid());
        await InsertSessionAsync(Guid.NewGuid(), otherTenant, Guid.NewGuid());
        var queue = new PostgresVoiceCallQueue(fixture.DataSource);
        await queue.EnqueuePendingSessionsAsync(requestedTenant, CancellationToken.None);
        await queue.EnqueuePendingSessionsAsync(otherTenant, CancellationToken.None);

        var claim = await queue.ClaimAsync(requestedTenant, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(requestedSession, claim!.SessionId);
    }

    [PostgresIntegrationFact]
    public async Task RetryAndDeadLetter_PersistAttemptState()
    {
        await fixture.ResetAsync();
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await InsertSessionAsync(sessionId, tenantId, Guid.NewGuid());
        var queue = new PostgresVoiceCallQueue(fixture.DataSource);
        await queue.EnqueuePendingSessionsAsync(tenantId, CancellationToken.None);
        var first = await queue.ClaimAsync(tenantId, TimeSpan.FromMinutes(5), CancellationToken.None);
        await queue.MarkRetryAsync(first!, TimeSpan.Zero, "rate limited", CancellationToken.None);
        var second = await queue.ClaimAsync(tenantId, TimeSpan.FromMinutes(5), CancellationToken.None);

        await queue.MarkDeadLetterAsync(second!, "provider failure", CancellationToken.None);

        await using var command = fixture.DataSource.CreateCommand(
            "select status, attempts, last_error from voice_call_queue where voice_call_session_id=$1");
        command.Parameters.AddWithValue(sessionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("dead_letter", reader.GetString(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal("provider failure", reader.GetString(2));
    }

    private async Task InsertSessionAsync(Guid sessionId, Guid tenantId, Guid agentTaskId)
    {
        await using var command = fixture.DataSource.CreateCommand(
            "insert into voice_call_sessions (id, tenant_id, agent_task_id, status) values ($1,$2,$3,'queued')");
        command.Parameters.AddWithValue(sessionId);
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(agentTaskId);
        await command.ExecuteNonQueryAsync();
    }
}
