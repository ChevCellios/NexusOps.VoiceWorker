using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using NexusOps.VoiceWorker.Realtime.OpenAI;

namespace NexusOps.VoiceWorker.Controllers;

[ApiController]
[Route("realtime/browser")]
[Authorize(Roles = "Administrator,Manager")]
[EnableRateLimiting("realtime")]
public sealed class BrowserRealtimeController(
    BrowserRealtimeSessionService service) : ControllerBase
{
    [HttpPost("session")]
    [Consumes("application/sdp")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var sdp = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sdp)) return BadRequest("SDP offer is required.");
        if (sdp.Length > 64 * 1024) return BadRequest("SDP offer is too large.");

        var result = await service.CreateAsync(sdp, cancellationToken);
        return result.Success
            ? Content(result.Body, "application/sdp", Encoding.UTF8)
            : StatusCode(StatusCodes.Status502BadGateway, result.Body);
    }

}
