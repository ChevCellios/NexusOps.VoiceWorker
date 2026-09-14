using NexusOps.VoiceWorker.Realtime;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;
using NexusOps.VoiceWorker.Security;
using NexusOps.VoiceWorker.Persistence;

namespace NexusOps.VoiceWorker.WebSockets;

public sealed class VoiceMediaSecurityOptions
{
    public const string SectionName = "VoiceMediaSecurity";
    public int MaxConcurrentStreams { get; init; } = 10;
    public int MaxSessionMinutes { get; init; } = 30;
}

public sealed class VoiceMediaWebSocketHandler(
    IRealtimeClient realtimeClient,
    ITwilioRequestValidator requestValidator,
    IOptions<TwilioOptions> options,
    IOptions<VoiceMediaSecurityOptions> securityOptions,
    IVoiceCallRepository calls,
    IConfiguration configuration,
    ILogger<VoiceMediaWebSocketHandler> logger)
{
    private readonly SemaphoreSlim _streamSlots = new(Math.Clamp(securityOptions.Value.MaxConcurrentStreams, 1, 100));

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

        if (!Guid.TryParse(context.Request.Query["voiceCallSessionId"], out var parsed))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        Guid? sessionId = parsed;
        var call = await calls.GetAsync(parsed, context.RequestAborted);
        if (call is null || !Guid.TryParse(configuration["NexusOps:TenantId"], out var tenantId) || call.TenantId != tenantId)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!await _streamSlots.WaitAsync(TimeSpan.Zero, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        logger.LogInformation("Twilio Media Stream WebSocket request accepted for voice session {SessionId}.", sessionId);
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var sessionTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            sessionTimeout.CancelAfter(TimeSpan.FromMinutes(Math.Clamp(securityOptions.Value.MaxSessionMinutes, 1, 120)));
            await realtimeClient.BridgeAsync(socket, sessionId, sessionTimeout.Token);
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
        finally
        {
            _streamSlots.Release();
        }
    }
}
