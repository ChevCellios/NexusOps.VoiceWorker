using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NexusOps.VoiceWorker.Realtime.OpenAI;

public sealed class BrowserRealtimeSessionService(
    HttpClient httpClient,
    IOptions<OpenAIRealtimeOptions> options,
    ILogger<BrowserRealtimeSessionService> logger)
{
    private readonly OpenAIRealtimeOptions _options = options.Value;

    public async Task<(bool Success, string Body)> CreateAsync(string sdp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.StartsWith("YOUR_", StringComparison.Ordinal))
            throw new InvalidOperationException("OpenAI is not configured.");

        var session = JsonSerializer.Serialize(new
        {
            type = "realtime",
            model = _options.RealtimeModel,
            instructions = _options.Instructions,
            audio = new { output = new { voice = _options.Voice } }
        });

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(sdp, Encoding.UTF8, "application/sdp"), "sdp");
        content.Add(new StringContent(session, Encoding.UTF8, "application/json"), "session");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/realtime/calls")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            logger.LogError("OpenAI browser Realtime session failed with HTTP {StatusCode}.",
                (int)response.StatusCode);
        return (response.IsSuccessStatusCode, body);
    }
}
