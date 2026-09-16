using Npgsql;

namespace NexusOps.VoiceWorker.Persistence;

public sealed record QueuedVoiceCall(Guid SessionId, Guid AgentTaskId, Guid LockId, int Attempt);

public interface IVoiceCallQueue
{
    Task EnqueuePendingSessionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<QueuedVoiceCall?> ClaimAsync(Guid tenantId, TimeSpan lease, CancellationToken cancellationToken);
    Task MarkCompletedAsync(QueuedVoiceCall call, CancellationToken cancellationToken);
    Task MarkRetryAsync(QueuedVoiceCall call, TimeSpan delay, string error, CancellationToken cancellationToken);
    Task MarkDeadLetterAsync(QueuedVoiceCall call, string error, CancellationToken cancellationToken);
}

public sealed class DisabledVoiceCallQueue : IVoiceCallQueue
{
    public Task EnqueuePendingSessionsAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<QueuedVoiceCall?> ClaimAsync(Guid tenantId, TimeSpan lease, CancellationToken cancellationToken) =>
        Task.FromResult<QueuedVoiceCall?>(null);
    public Task MarkCompletedAsync(QueuedVoiceCall call, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task MarkRetryAsync(QueuedVoiceCall call, TimeSpan delay, string error, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task MarkDeadLetterAsync(QueuedVoiceCall call, string error, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class PostgresVoiceCallQueue(NpgsqlDataSource dataSource) : IVoiceCallQueue
{
    public async Task EnqueuePendingSessionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string sql = """
            insert into voice_call_queue (voice_call_session_id, tenant_id, agent_task_id)
            select id, tenant_id, agent_task_id
            from voice_call_sessions
            where tenant_id = $1 and status = 'queued'
            on conflict (voice_call_session_id) do nothing
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(tenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<QueuedVoiceCall?> ClaimAsync(
        Guid tenantId, TimeSpan lease, CancellationToken cancellationToken)
    {
        var lockId = Guid.NewGuid();
        const string sql = """
            with candidate as (
                select voice_call_session_id
                from voice_call_queue
                where tenant_id = $1
                  and (
                    (status = 'pending' and available_at <= now()) or
                    (status = 'processing' and locked_at < now() - $2::interval)
                  )
                order by available_at, created_at
                for update skip locked
                limit 1
            )
            update voice_call_queue queue
            set status = 'processing', attempts = attempts + 1, locked_at = now(), lock_id = $3, updated_at = now()
            from candidate
            where queue.voice_call_session_id = candidate.voice_call_session_id
            returning queue.voice_call_session_id, queue.agent_task_id, queue.attempts
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(lease);
        command.Parameters.AddWithValue(lockId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetGuid(1), lockId, reader.GetInt32(2))
            : null;
    }

    public Task MarkCompletedAsync(QueuedVoiceCall call, CancellationToken cancellationToken) =>
        UpdateAsync(call, "completed", null, TimeSpan.Zero, cancellationToken);

    public Task MarkRetryAsync(
        QueuedVoiceCall call, TimeSpan delay, string error, CancellationToken cancellationToken) =>
        UpdateAsync(call, "pending", error, delay, cancellationToken);

    public Task MarkDeadLetterAsync(QueuedVoiceCall call, string error, CancellationToken cancellationToken) =>
        UpdateAsync(call, "dead_letter", error, TimeSpan.Zero, cancellationToken);

    private async Task UpdateAsync(
        QueuedVoiceCall call,
        string status,
        string? error,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update voice_call_queue
            set status = $3,
                available_at = case when $3 = 'pending' then now() + $4::interval else available_at end,
                locked_at = null,
                lock_id = null,
                last_error = $5,
                completed_at = case when $3 in ('completed', 'dead_letter') then now() else null end,
                updated_at = now()
            where voice_call_session_id = $1 and lock_id = $2 and status = 'processing'
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(call.SessionId);
        command.Parameters.AddWithValue(call.LockId);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(delay);
        command.Parameters.AddWithValue((object?)Truncate(error) ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Voice queue lease for session {call.SessionId} was lost.");
    }

    private static string? Truncate(string? value) => value is { Length: > 2000 } ? value[..2000] : value;
}
