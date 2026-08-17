using NexusOps.VoiceWorker.Models;

namespace NexusOps.VoiceWorker.Providers;

public interface IVoiceProvider
{
    string Name { get; }
    Task<string?> StartCallAsync(VoiceCallSession session, CancellationToken cancellationToken);
    string GetMediaStreamUrl(Guid voiceCallSessionId);
}
