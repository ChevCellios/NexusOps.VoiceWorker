using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Models;

namespace NexusOps.VoiceWorker.Providers.Twilio;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";
    public string AccountSid { get; init; } = string.Empty;
    public string AuthToken { get; init; } = string.Empty;
    public string FromPhoneNumber { get; init; } = string.Empty;
    public string PublicBaseUrl { get; init; } = string.Empty;
    public string MediaStreamUrl { get; init; } = string.Empty;
}

public sealed class TwilioVoiceProvider(
    HttpClient httpClient,
    IOptions<TwilioOptions> options,
    ILogger<TwilioVoiceProvider> logger)
    : IVoiceProvider
{
    private readonly TwilioOptions _options = options.Value;
    public string Name => "twilio";

    public async Task<string?> StartCallAsync(VoiceCallSession session, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(session.ToNumber))
            throw new InvalidOperationException("The voice call session has no destination number.");

        var sessionId = session.Id.ToString("D");
        var answerUrl = $"{_options.PublicBaseUrl.TrimEnd('/')}/voice/provider/answer?voiceCallSessionId={sessionId}";
        var statusUrl = $"{_options.PublicBaseUrl.TrimEnd('/')}/voice/provider/status?voiceCallSessionId={sessionId}";
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", session.ToNumber),
            new("From", string.IsNullOrWhiteSpace(session.FromNumber) ? _options.FromPhoneNumber : session.FromNumber),
            new("Url", answerUrl),
            new("Method", "POST"),
            new("StatusCallback", statusUrl),
            new("StatusCallbackMethod", "POST"),
            new("StatusCallbackEvent", "initiated"),
            new("StatusCallbackEvent", "ringing"),
            new("StatusCallbackEvent", "answered"),
            new("StatusCallbackEvent", "completed"),
            new("Record", session.RecordingEnabled ? "true" : "false")
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Calls.json")
        {
            Content = new FormUrlEncodedContent(values)
        };
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Twilio rejected call {SessionId} with HTTP {StatusCode}: {Response}",
                session.Id, (int)response.StatusCode, body);
            throw new HttpRequestException("Twilio rejected the outbound call request.", null, response.StatusCode);
        }

        using var json = JsonDocument.Parse(body);
        var callSid = json.RootElement.GetProperty("sid").GetString()
            ?? throw new InvalidOperationException("Twilio response did not contain a call SID.");
        logger.LogInformation("Twilio call {CallSid} created for voice session {SessionId}.", callSid, session.Id);
        return callSid;
    }

    public string GetMediaStreamUrl(Guid voiceCallSessionId) =>
        $"{_options.MediaStreamUrl}?voiceCallSessionId={voiceCallSessionId:D}";

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || _options.AccountSid.StartsWith("YOUR_", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) || _options.AuthToken.StartsWith("YOUR_", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(_options.PublicBaseUrl) || string.IsNullOrWhiteSpace(_options.MediaStreamUrl))
            throw new InvalidOperationException("Twilio is not configured. Supply credentials and public callback URLs through secrets.");
    }
}
