using System.Net.WebSockets;

namespace NexusOps.VoiceWorker.Realtime;

public interface IRealtimeClient
{
    Task BridgeAsync(WebSocket providerSocket, Guid? voiceCallSessionId, CancellationToken cancellationToken);
}
