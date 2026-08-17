namespace NexusOps.VoiceWorker.Security;

public interface IVoiceRequestAuthorizer
{
    Task<bool> CanAccessCallAsync(Guid voiceCallSessionId, CancellationToken cancellationToken);
}

// Replace with NexusOps authentication, tenant permissions and webhook signature validation.
public sealed class DevelopmentVoiceRequestAuthorizer : IVoiceRequestAuthorizer
{
    public Task<bool> CanAccessCallAsync(Guid voiceCallSessionId, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
