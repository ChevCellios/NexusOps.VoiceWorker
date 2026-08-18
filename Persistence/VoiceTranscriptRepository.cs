using Npgsql;

namespace NexusOps.VoiceWorker.Persistence;

public sealed record VoiceTranscriptMessage(int SequenceNumber, string Speaker, string Text, DateTimeOffset CreatedAt);

public interface IVoiceTranscriptRepository
{
    Task AppendAsync(Guid voiceCallSessionId, string speaker, string text, CancellationToken cancellationToken);
    Task<IReadOnlyList<VoiceTranscriptMessage>> ListAsync(Guid voiceCallSessionId, CancellationToken cancellationToken);
}

public sealed class InMemoryVoiceTranscriptRepository : IVoiceTranscriptRepository
{
    private readonly List<(Guid SessionId, VoiceTranscriptMessage Message)> _messages = [];
    private readonly Lock _lock = new();

    public Task AppendAsync(Guid voiceCallSessionId, string speaker, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var sequence = _messages.Count(item => item.SessionId == voiceCallSessionId) + 1;
            _messages.Add((voiceCallSessionId, new(sequence, speaker, text, DateTimeOffset.UtcNow)));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VoiceTranscriptMessage>> ListAsync(Guid voiceCallSessionId, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<VoiceTranscriptMessage>>(
                _messages.Where(item => item.SessionId == voiceCallSessionId).Select(item => item.Message).ToArray());
    }
}

public sealed class PostgresVoiceTranscriptRepository(NpgsqlDataSource dataSource) : IVoiceTranscriptRepository
{
    public async Task<IReadOnlyList<VoiceTranscriptMessage>> ListAsync(Guid voiceCallSessionId, CancellationToken cancellationToken)
    {
        const string sql = "select sequence_number, speaker, message_text, created_at from voice_call_messages where voice_call_session_id = $1 order by sequence_number";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(voiceCallSessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var messages = new List<VoiceTranscriptMessage>();
        while (await reader.ReadAsync(cancellationToken))
            messages.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return messages;
    }

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
