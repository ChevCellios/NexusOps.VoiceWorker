using System.Security;
using Microsoft.AspNetCore.Mvc;
using NexusOps.VoiceWorker.Models;
using NexusOps.VoiceWorker.Providers;
using NexusOps.VoiceWorker.Security;
using NexusOps.VoiceWorker.Services;

namespace NexusOps.VoiceWorker.Controllers;

[ApiController]
[Route("voice/provider")]
public sealed class VoiceProviderController(
    IVoiceCallService service,
    IVoiceProvider provider,
    ITwilioRequestValidator requestValidator,
    ILogger<VoiceProviderController> logger) : ControllerBase
{
    [HttpPost("status")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Status(
        [FromQuery] Guid voiceCallSessionId,
        [FromForm] TwilioStatusWebhookRequest webhook,
        CancellationToken cancellationToken)
    {
        if (!await requestValidator.IsValidAsync(Request, cancellationToken)) return Unauthorized();

        var request = new ProviderStatusRequest(webhook.CallSid, webhook.CallStatus, voiceCallSessionId);
        var session = await service.ApplyProviderStatusAsync(request, cancellationToken);
        return session is null ? NotFound() : Ok();
    }

    [HttpPost("answer")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Answer([FromQuery] Guid voiceCallSessionId, CancellationToken cancellationToken)
    {
        if (!await requestValidator.IsValidAsync(Request, cancellationToken))
        {
            logger.LogWarning("Twilio answer webhook was rejected for voice session {SessionId}.", voiceCallSessionId);
            return Unauthorized();
        }

        var streamUrl = SecurityElement.Escape(provider.GetMediaStreamUrl(voiceCallSessionId));
        logger.LogInformation("Twilio answer webhook accepted for voice session {SessionId}; returning Media Stream TwiML.", voiceCallSessionId);
        var twiml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response><Connect><Stream url=\"{streamUrl}\"><Parameter name=\"voiceCallSessionId\" value=\"{voiceCallSessionId:D}\" /></Stream></Connect></Response>";
        return Content(twiml, "text/xml", System.Text.Encoding.UTF8);
    }
}
