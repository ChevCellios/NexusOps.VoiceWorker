using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Persistence;

namespace NexusOps.VoiceWorker.Realtime.OpenAI;

public sealed class OpenAIRealtimeOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; init; } = string.Empty;
    public string RealtimeModel { get; init; } = "gpt-realtime-1.5";
    public string RealtimeEndpoint { get; init; } = "wss://api.openai.com/v1/realtime";
    public string Voice { get; init; } = "alloy";
    public string Instructions { get; init; } = "Govori hrvatski, budi jasan i sažet. Ne izvršavaj poslovne radnje bez potvrde korisnika.";
    public string TranscriptionModel { get; init; } = "gpt-4o-mini-transcribe";
    public string TranscriptionLanguage { get; init; } = "hr";
}

public sealed class OpenAIRealtimeClient(
    IOptions<OpenAIRealtimeOptions> options,
    IVoiceTranscriptRepository transcriptRepository,
    ILogger<OpenAIRealtimeClient> logger) : IRealtimeClient
{
    private readonly OpenAIRealtimeOptions _options = options.Value;

    public async Task BridgeAsync(WebSocket providerSocket, Guid? voiceCallSessionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var openAiSocket = new ClientWebSocket();
        openAiSocket.Options.SetRequestHeader("Authorization", $"Bearer {_options.ApiKey}");
        var endpoint = $"{_options.RealtimeEndpoint.TrimEnd('/')}?model={Uri.EscapeDataString(_options.RealtimeModel)}";
        logger.LogInformation("Connecting OpenAI Realtime bridge for {SessionId} using {Model}.",
            voiceCallSessionId, _options.RealtimeModel);
        await openAiSocket.ConnectAsync(new Uri(endpoint), cancellationToken);

        logger.LogInformation("OpenAI Realtime bridge connected for {SessionId} using {Model}.",
            voiceCallSessionId, _options.RealtimeModel);

        await SendJsonAsync(openAiSocket, new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "text", "audio" },
                instructions = _options.Instructions,
                voice = _options.Voice,
                input_audio_format = "g711_ulaw",
                output_audio_format = "g711_ulaw",
                input_audio_transcription = new
                {
                    model = _options.TranscriptionModel,
                    language = _options.TranscriptionLanguage
                },
                turn_detection = new { type = "server_vad", create_response = true }
            }
        }, cancellationToken);

        var streamSid = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bridgeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fromTwilio = RelayTwilioToOpenAiAsync(providerSocket, openAiSocket, streamSid, bridgeCancellation.Token);
        var fromOpenAi = RelayOpenAiToTwilioAsync(
            openAiSocket, providerSocket, streamSid, voiceCallSessionId, bridgeCancellation.Token);

        await Task.WhenAny(fromTwilio, fromOpenAi);
        await bridgeCancellation.CancelAsync();
        try { await Task.WhenAll(fromTwilio, fromOpenAi); }
        catch (OperationCanceledException) when (bridgeCancellation.IsCancellationRequested) { }

        await CloseIfOpenAsync(openAiSocket, "Media bridge ended", CancellationToken.None);
        await CloseIfOpenAsync(providerSocket, "Media bridge ended", CancellationToken.None);
    }

    private async Task RelayTwilioToOpenAiAsync(
        WebSocket twilio,
        WebSocket openAi,
        TaskCompletionSource<string> streamSid,
        CancellationToken cancellationToken)
    {
        while (twilio.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(twilio, cancellationToken);
            if (message is null) break;
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            var eventType = root.GetProperty("event").GetString();

            if (eventType == "start")
            {
                var sid = root.GetProperty("start").GetProperty("streamSid").GetString();
                if (!string.IsNullOrWhiteSpace(sid)) streamSid.TrySetResult(sid);
            }
            else if (eventType == "media")
            {
                var payload = root.GetProperty("media").GetProperty("payload").GetString();
                if (!string.IsNullOrWhiteSpace(payload))
                    await SendJsonAsync(openAi, new { type = "input_audio_buffer.append", audio = payload }, cancellationToken);
            }
            else if (eventType == "stop")
            {
                break;
            }
        }
    }

    private async Task RelayOpenAiToTwilioAsync(
        WebSocket openAi,
        WebSocket twilio,
        TaskCompletionSource<string> streamSid,
        Guid? voiceCallSessionId,
        CancellationToken cancellationToken)
    {
        while (openAi.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var message = await ReceiveTextAsync(openAi, cancellationToken);
            if (message is null) break;
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "response.output_audio.delta" && root.TryGetProperty("delta", out var delta))
            {
                var sid = await streamSid.Task.WaitAsync(cancellationToken);
                await SendJsonAsync(twilio, new
                {
                    @event = "media",
                    streamSid = sid,
                    media = new { payload = delta.GetString() }
                }, cancellationToken);
            }
            else if (type == "input_audio_buffer.speech_started" && streamSid.Task.IsCompletedSuccessfully)
            {
                await SendJsonAsync(twilio, new { @event = "clear", streamSid = streamSid.Task.Result }, cancellationToken);
                await SendJsonAsync(openAi, new { type = "response.cancel" }, cancellationToken);
            }
            else if (type == "conversation.item.input_audio_transcription.completed")
            {
                await PersistTranscriptAsync(voiceCallSessionId, "user", root, cancellationToken);
            }
            else if (type == "response.output_audio_transcript.done")
            {
                await PersistTranscriptAsync(voiceCallSessionId, "agent", root, cancellationToken);
            }
            else if (type == "error")
            {
                logger.LogError("OpenAI Realtime error for stream {StreamSid}: {Error}",
                    streamSid.Task.IsCompletedSuccessfully ? streamSid.Task.Result : "pending", message);
            }
        }
    }

    private async Task PersistTranscriptAsync(
        Guid? voiceCallSessionId,
        string speaker,
        JsonElement realtimeEvent,
        CancellationToken cancellationToken)
    {
        if (voiceCallSessionId is null ||
            !realtimeEvent.TryGetProperty("transcript", out var transcript) ||
            string.IsNullOrWhiteSpace(transcript.GetString())) return;

        try
        {
            await transcriptRepository.AppendAsync(
                voiceCallSessionId.Value, speaker, transcript.GetString()!, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to persist {Speaker} transcript for {SessionId}.",
                speaker, voiceCallSessionId);
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("Only text WebSocket frames are supported by the media bridge.");
            content.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(content.ToArray());
    }

    private static Task SendJsonAsync(WebSocket socket, object value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task CloseIfOpenAsync(WebSocket socket, string reason, CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.StartsWith("YOUR_", StringComparison.Ordinal))
            throw new InvalidOperationException("OpenAI is not configured. Supply OpenAI:ApiKey through secrets.");
    }
}
