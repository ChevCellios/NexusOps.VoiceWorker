using Npgsql;

namespace NexusOps.VoiceWorker.Persistence;

public interface IVoiceTranscriptRepository
{
    Task AppendAsync(Guid voiceCallSessionId, string speaker, string text, CancellationToken cancellationToken);
}

public sealed class InMemoryVoiceTranscriptRepository : IVoiceTranscriptRepository
{
    private readonly List<(Guid SessionId, string Speaker, string Text)> _messages = [];
    private readonly Lock _lock = new();

    public Task AppendAsync(Guid voiceCallSessionId, string speaker, string text, CancellationToken cancellationToken)
    {
        lock (_lock) _messages.Add((voiceCallSessionId, speaker, text));
        return Task.CompletedTask;
    }
}

public sealed class PostgresVoiceTranscriptRepository(NpgsqlDataSource dataSource) : IVoiceTranscriptRepository
{
    public async Task AppendAsync(Guid voiceCallSessionId, string speaker, string text, CancellationToken cancellationToken)
    {
        if (speaker is not ("user" or "agent" or "system"))
            throw new ArgumentOutOfRangeException(nameof(speaker), "Unknown transcript speaker.");
        if (string.IsNullOrWhiteSpace(text)) return;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended($1::text, 0))", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue(voiceCallSessionId);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string sql = """
            insert into voice_call_messages (
                tenant_id, voice_call_session_id, sequence_number, speaker, message_text
            )
            select
                sessions.tenant_id,
                sessions.id,
                coalesce((
                    select max(messages.sequence_number) + 1
                    from voice_call_messages messages
                    where messages.voice_call_session_id = sessions.id
                ), 1),
                $2,
                $3
            from voice_call_sessions sessions
            where sessions.id = $1
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(voiceCallSessionId);
        command.Parameters.AddWithValue(speaker);
        command.Parameters.AddWithValue(text.Trim());
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1) throw new InvalidOperationException($"Voice call session {voiceCallSessionId} was not found.");
        await transaction.CommitAsync(cancellationToken);
    }
}
