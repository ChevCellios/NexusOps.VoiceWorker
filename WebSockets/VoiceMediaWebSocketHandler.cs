using NexusOps.VoiceWorker.Realtime;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;
using NexusOps.VoiceWorker.Security;

namespace NexusOps.VoiceWorker.WebSockets;

public sealed class VoiceMediaWebSocketHandler(
    IRealtimeClient realtimeClient,
    ITwilioRequestValidator requestValidator,
    IOptions<TwilioOptions> options,
    ILogger<VoiceMediaWebSocketHandler> logger)
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
        logger.LogInformation("Twilio Media Stream WebSocket request accepted for voice session {SessionId}.", sessionId);
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            await realtimeClient.BridgeAsync(socket, sessionId, context.RequestAborted);
            logger.LogInformation("Voice media bridge ended normally for voice session {SessionId}.", sessionId);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Voice media bridge disconnected for voice session {SessionId}.", sessionId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Voice media bridge failed for voice session {SessionId}.", sessionId);
        }
    }
}
