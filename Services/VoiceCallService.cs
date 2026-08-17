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

        var providerCallId = await provider.StartCallAsync(session, cancellationToken);
        session = session with
        {
            Status = VoiceCallStatus.Initiating,
            Provider = provider.Name,
            ProviderCallId = providerCallId,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repository.UpsertAsync(session, cancellationToken);
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

    public Task<VoiceCallSession?> ApplyProviderStatusAsync(
        ProviderStatusRequest request, CancellationToken cancellationToken) =>
        request.VoiceCallSessionId is null
            ? Task.FromResult<VoiceCallSession?>(null)
            : SetStatusAsync(request.VoiceCallSessionId.Value, MapProviderStatus(request.Status), null, cancellationToken);

    private static VoiceCallStatus MapProviderStatus(string status) => status.ToLowerInvariant() switch
    {
        "queued" => VoiceCallStatus.Queued,
        "initiated" => VoiceCallStatus.Initiating,
        "ringing" => VoiceCallStatus.Ringing,
        "in-progress" => VoiceCallStatus.InProgress,
        "completed" => VoiceCallStatus.Completed,
        "busy" => VoiceCallStatus.Busy,
        "no-answer" => VoiceCallStatus.NoAnswer,
        "canceled" => VoiceCallStatus.Cancelled,
        _ => VoiceCallStatus.Failed
    };

    private static string ToApiStatus(VoiceCallStatus status) => status switch
    {
        VoiceCallStatus.InProgress => "in_progress",
        VoiceCallStatus.NoAnswer => "no_answer",
        _ => status.ToString().ToLowerInvariant()
    };
}
