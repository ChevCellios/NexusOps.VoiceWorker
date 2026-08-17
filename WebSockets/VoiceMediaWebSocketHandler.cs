using NexusOps.VoiceWorker.Realtime;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;
using NexusOps.VoiceWorker.Security;

namespace NexusOps.VoiceWorker.WebSockets;

public sealed class VoiceMediaWebSocketHandler(
    IRealtimeClient realtimeClient,
    ITwilioRequestValidator requestValidator,
    IOptions<TwilioOptions> options)
{
    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("A WebSocket upgrade request is required.");
            return;
        }

        var externallyVisibleUrl = $"{options.Value.MediaStreamUrl.Split('?')[0]}{context.Request.QueryString}";
        if (!await requestValidator.IsValidAsync(context.Request, context.RequestAborted, externallyVisibleUrl))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Guid? sessionId = Guid.TryParse(context.Request.Query["voiceCallSessionId"], out var parsed)
            ? parsed
            : null;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await realtimeClient.BridgeAsync(socket, sessionId, context.RequestAborted);
    }
}
