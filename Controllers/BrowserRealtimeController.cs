using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Realtime.OpenAI;

namespace NexusOps.VoiceWorker.Controllers;

[ApiController]
[Route("realtime/browser")]
[Authorize(Roles = "Administrator,Manager")]
[EnableRateLimiting("realtime")]
public sealed class BrowserRealtimeController(
    BrowserRealtimeSessionService service,
    IOptions<BrowserRealtimeTestOptions> testOptions) : ControllerBase
{
    [HttpPost("session")]
    [Consumes("application/sdp")]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        var configuredKey = testOptions.Value.AccessKey;
        var suppliedKey = Request.Headers["X-NexusOps-Test-Key"].ToString();
        if (!IsValidKey(configuredKey, suppliedKey)) return Unauthorized();

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var sdp = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sdp)) return BadRequest("SDP offer is required.");

        var result = await service.CreateAsync(sdp, cancellationToken);
        return result.Success
            ? Content(result.Body, "application/sdp", Encoding.UTF8)
            : StatusCode(StatusCodes.Status502BadGateway, result.Body);
    }

    private static bool IsValidKey(string configured, string supplied)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(supplied)) return false;
        var expected = Encoding.UTF8.GetBytes(configured);
        var actual = Encoding.UTF8.GetBytes(supplied);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
