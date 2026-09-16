using NexusOps.VoiceWorker.Models;
using NexusOps.VoiceWorker.Persistence;
using NexusOps.VoiceWorker.Providers;

namespace NexusOps.VoiceWorker.Services;

public interface IVoiceCallService
{
    Task<StartVoiceCallResponse> StartAsync(StartVoiceCallRequest request, CancellationToken cancellationToken);
    Task<VoiceCallSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<VoiceCallSession?> SetStatusAsync(Guid id, VoiceCallStatus status, string? detail, CancellationToken cancellationToken);
    Task<VoiceCallSession?> SetOutcomeAsync(Guid id, VoiceCallOutcomeRequest request, CancellationToken cancellationToken);
    Task<VoiceCallSession?> ApplyProviderStatusAsync(ProviderStatusRequest request, CancellationToken cancellationToken);
}

public sealed class VoiceCallService(IVoiceCallRepository repository, IVoiceProvider provider) : IVoiceCallService
{
    public async Task<StartVoiceCallResponse> StartAsync(StartVoiceCallRequest request, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(request.VoiceCallSessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Voice call session {request.VoiceCallSessionId} was not found.");
        if (session.AgentTaskId != request.AgentTaskId)
            throw new InvalidOperationException("The voice call session does not belong to the supplied agent task.");
        if (session.Status != VoiceCallStatus.Queued)
            throw new InvalidOperationException($"A call cannot be started from status {session.Status}.");

        var queuedSession = session;
        session = queuedSession with
        {
            Status = VoiceCallStatus.Initiating,
            Provider = provider.Name,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (!await repository.TryUpdateAsync(
                session, queuedSession.Status, queuedSession.ProviderCallId, cancellationToken))
            throw new InvalidOperationException("The voice call session was already started by another request.");

        string? providerCallId;
        try
        {
            providerCallId = await provider.StartCallAsync(session, cancellationToken);
        }
        catch
        {
            var failedSession = session with
            {
                Status = VoiceCallStatus.Failed,
                FailureReason = "The voice provider could not start the call.",
                EndedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await repository.TryUpdateAsync(
                failedSession, session.Status, session.ProviderCallId, CancellationToken.None);
            throw;
        }

        var startedSession = session with
        {
            ProviderCallId = providerCallId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (!await repository.TryUpdateAsync(
                startedSession, session.Status, session.ProviderCallId, cancellationToken))
            throw new InvalidOperationException("The voice call session changed while the provider call was starting.");

        session = startedSession;
        return new(session.Id, ToApiStatus(session.Status), provider.Name, providerCallId);
    }

    public Task<VoiceCallSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        repository.GetAsync(id, cancellationToken);

    public async Task<VoiceCallSession?> SetStatusAsync(
        Guid id, VoiceCallStatus status, string? detail, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(id, cancellationToken);
        if (session is null) return null;
        session = session with
        {
            Status = status,
            FailureReason = status == VoiceCallStatus.Failed ? detail : session.FailureReason,
            EndedAt = status is VoiceCallStatus.Completed or VoiceCallStatus.Failed or
                VoiceCallStatus.Busy or VoiceCallStatus.NoAnswer or VoiceCallStatus.Cancelled
                ? DateTimeOffset.UtcNow
                : session.EndedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repository.UpsertAsync(session, cancellationToken);
        return session;
    }

    public async Task<VoiceCallSession?> SetOutcomeAsync(
        Guid id, VoiceCallOutcomeRequest request, CancellationToken cancellationToken)
    {
        var session = await repository.GetAsync(id, cancellationToken);
        if (session is null) return null;
        session = session with { Outcome = request.Outcome, UpdatedAt = DateTimeOffset.UtcNow };
        await repository.UpsertAsync(session, cancellationToken);
        return session;
    }

    public async Task<VoiceCallSession?> ApplyProviderStatusAsync(
        ProviderStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.VoiceCallSessionId is null) return null;
        var session = await repository.GetAsync(request.VoiceCallSessionId.Value, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(session.ProviderCallId) ||
            !string.Equals(session.ProviderCallId, request.ProviderCallId, StringComparison.Ordinal))
            return null;

        if (!TryMapProviderStatus(request.Status, out var nextStatus)) return session;
        if (nextStatus == session.Status || !CanApplyProviderStatus(session.Status, nextStatus)) return session;

        var updated = session with
        {
            Status = nextStatus,
            AnsweredAt = nextStatus is VoiceCallStatus.Answered or VoiceCallStatus.InProgress
                ? session.AnsweredAt ?? DateTimeOffset.UtcNow
                : session.AnsweredAt,
            EndedAt = IsTerminal(nextStatus) ? session.EndedAt ?? DateTimeOffset.UtcNow : session.EndedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return await repository.TryUpdateAsync(
            updated, session.Status, session.ProviderCallId, cancellationToken)
            ? updated
            : await repository.GetAsync(session.Id, cancellationToken);
    }

    private static bool TryMapProviderStatus(string status, out VoiceCallStatus mappedStatus)
    {
        mappedStatus = status.ToLowerInvariant() switch
        {
            "queued" => VoiceCallStatus.Queued,
            "initiated" => VoiceCallStatus.Initiating,
            "ringing" => VoiceCallStatus.Ringing,
            "in-progress" => VoiceCallStatus.InProgress,
            "completed" => VoiceCallStatus.Completed,
            "failed" => VoiceCallStatus.Failed,
            "busy" => VoiceCallStatus.Busy,
            "no-answer" => VoiceCallStatus.NoAnswer,
            "canceled" => VoiceCallStatus.Cancelled,
            _ => default
        };
        return status.Equals("queued", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("initiated", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("ringing", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("in-progress", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("busy", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("no-answer", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanApplyProviderStatus(VoiceCallStatus current, VoiceCallStatus next) => current switch
    {
        VoiceCallStatus.Initiating => next is VoiceCallStatus.Ringing or VoiceCallStatus.Answered or
            VoiceCallStatus.InProgress || IsTerminal(next),
        VoiceCallStatus.Ringing => next is VoiceCallStatus.Answered or VoiceCallStatus.InProgress || IsTerminal(next),
        VoiceCallStatus.Answered => next == VoiceCallStatus.InProgress || IsTerminal(next),
        VoiceCallStatus.InProgress => IsTerminal(next),
        _ => false
    };

    private static bool IsTerminal(VoiceCallStatus status) => status is VoiceCallStatus.Completed or
        VoiceCallStatus.Failed or VoiceCallStatus.Busy or VoiceCallStatus.NoAnswer or VoiceCallStatus.Cancelled;

    private static string ToApiStatus(VoiceCallStatus status) => status switch
    {
        VoiceCallStatus.InProgress => "in_progress",
        VoiceCallStatus.NoAnswer => "no_answer",
        _ => status.ToString().ToLowerInvariant()
    };
}
