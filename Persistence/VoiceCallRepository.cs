using System.Collections.Concurrent;
using NexusOps.VoiceWorker.Models;
using Npgsql;

namespace NexusOps.VoiceWorker.Persistence;

public interface IVoiceCallRepository
{
    Task<VoiceCallSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<VoiceCallSession>> ListAsync(int limit, CancellationToken cancellationToken);
    Task UpsertAsync(VoiceCallSession session, CancellationToken cancellationToken);
    Task<bool> TryUpdateAsync(
        VoiceCallSession session,
        VoiceCallStatus expectedStatus,
        string? expectedProviderCallId,
        CancellationToken cancellationToken);
    Task<bool> TryRequeueAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class InMemoryVoiceCallRepository : IVoiceCallRepository
{
    private readonly ConcurrentDictionary<Guid, VoiceCallSession> _sessions = new();
    private readonly Lock _lock = new();

    public Task<VoiceCallSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<VoiceCallSession>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VoiceCallSession>>(
            _sessions.Values.OrderByDescending(item => item.CreatedAt).Take(limit).ToArray());

    public Task UpsertAsync(VoiceCallSession session, CancellationToken cancellationToken)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateAsync(
        VoiceCallSession session,
        VoiceCallStatus expectedStatus,
        string? expectedProviderCallId,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(session.Id, out var current) ||
                current.Status != expectedStatus ||
                !string.Equals(current.ProviderCallId, expectedProviderCallId, StringComparison.Ordinal))
                return Task.FromResult(false);

            _sessions[session.Id] = session;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryRequeueAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(id, out var current) || current.Status != VoiceCallStatus.Failed ||
                current.ProviderCallId is not null)
                return Task.FromResult(false);
            _sessions[id] = current with
            {
                Status = VoiceCallStatus.Queued,
                FailureReason = null,
                StartedAt = null,
                EndedAt = null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult(true);
        }
    }
}

public sealed class PostgresVoiceCallRepository(NpgsqlDataSource dataSource) : IVoiceCallRepository
{
    private const string SelectSql = """
        select id, tenant_id, organization_id, agent_task_id, agent_id,
               call_direction, status, provider, provider_call_id, from_number,
               to_number, contact_name, language_code, purpose, initial_instruction,
               recording_enabled, consent_required, consent_obtained, started_at,
               answered_at, ended_at, duration_seconds, result_code, result_summary,
               created_at, updated_at
        from voice_call_sessions
        where id = $1
        """;

    public async Task<VoiceCallSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(SelectSql);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return ReadSession(reader);
    }

    public async Task<IReadOnlyList<VoiceCallSession>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var sql = SelectSql.Replace("where id = $1", "order by created_at desc limit $1", StringComparison.Ordinal);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(Math.Clamp(limit, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sessions = new List<VoiceCallSession>();
        while (await reader.ReadAsync(cancellationToken)) sessions.Add(ReadSession(reader));
        return sessions;
    }

    private static VoiceCallSession ReadSession(NpgsqlDataReader reader) => new(
            reader.GetGuid(0), reader.GetGuid(1), GetNullableGuid(reader, 2),
            reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5),
            ParseStatus(reader.GetString(6)), reader.GetString(7), GetNullableString(reader, 8),
            GetNullableString(reader, 9), GetNullableString(reader, 10), GetNullableString(reader, 11),
            reader.GetString(12), GetNullableString(reader, 13), GetNullableString(reader, 14),
            reader.GetBoolean(15), reader.GetBoolean(16), reader.GetBoolean(17),
            GetNullableDateTimeOffset(reader, 18), GetNullableDateTimeOffset(reader, 19),
            GetNullableDateTimeOffset(reader, 20), reader.IsDBNull(21) ? null : reader.GetInt32(21),
            GetNullableString(reader, 22), GetNullableString(reader, 23),
            reader.GetFieldValue<DateTimeOffset>(24), reader.GetFieldValue<DateTimeOffset>(25));

    public async Task UpsertAsync(VoiceCallSession session, CancellationToken cancellationToken)
    {
        const string sql = """
            update voice_call_sessions
            set status = $2,
                provider = $3,
                provider_call_id = $4,
                result_code = $5,
                result_summary = $6,
                started_at = $7,
                answered_at = $8,
                ended_at = $9,
                duration_seconds = $10,
                updated_at = now()
            where id = $1
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(session.Id);
        command.Parameters.AddWithValue(ToDatabaseStatus(session.Status));
        command.Parameters.AddWithValue(session.Provider);
        command.Parameters.AddWithValue((object?)session.ProviderCallId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.Outcome ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.StartedAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.AnsweredAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.EndedAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.DurationSeconds ?? DBNull.Value);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1) throw new InvalidOperationException($"Voice call session {session.Id} was not found.");
    }

    public async Task<bool> TryUpdateAsync(
        VoiceCallSession session,
        VoiceCallStatus expectedStatus,
        string? expectedProviderCallId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update voice_call_sessions
            set status = $2,
                provider = $3,
                provider_call_id = $4,
                result_code = $5,
                result_summary = $6,
                started_at = $7,
                answered_at = $8,
                ended_at = $9,
                duration_seconds = $10,
                updated_at = now()
            where id = $1
              and status = $11
              and provider_call_id is not distinct from $12
            """;

        await using var command = dataSource.CreateCommand(sql);
        AddUpdateParameters(command, session);
        command.Parameters.AddWithValue(ToDatabaseStatus(expectedStatus));
        command.Parameters.AddWithValue((object?)expectedProviderCallId ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryRequeueAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            update voice_call_sessions
            set status = 'queued', result_summary = null, started_at = null, ended_at = null, updated_at = now()
            where id = $1 and status = 'failed' and provider_call_id is null
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void AddUpdateParameters(NpgsqlCommand command, VoiceCallSession session)
    {
        command.Parameters.AddWithValue(session.Id);
        command.Parameters.AddWithValue(ToDatabaseStatus(session.Status));
        command.Parameters.AddWithValue(session.Provider);
        command.Parameters.AddWithValue((object?)session.ProviderCallId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.Outcome ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.StartedAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.AnsweredAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.EndedAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)session.DurationSeconds ?? DBNull.Value);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static VoiceCallStatus ParseStatus(string status) => status switch
    {
        "queued" => VoiceCallStatus.Queued,
        "initiating" => VoiceCallStatus.Initiating,
        "ringing" => VoiceCallStatus.Ringing,
        "answered" => VoiceCallStatus.Answered,
        "in_progress" => VoiceCallStatus.InProgress,
        "completed" => VoiceCallStatus.Completed,
        "failed" => VoiceCallStatus.Failed,
        "busy" => VoiceCallStatus.Busy,
        "no_answer" => VoiceCallStatus.NoAnswer,
        "cancelled" => VoiceCallStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown voice call status '{status}'.")
    };

    private static string ToDatabaseStatus(VoiceCallStatus status) => status switch
    {
        VoiceCallStatus.InProgress => "in_progress",
        VoiceCallStatus.NoAnswer => "no_answer",
        _ => status.ToString().ToLowerInvariant()
    };
}
