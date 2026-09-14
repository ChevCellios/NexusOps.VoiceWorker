using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using NexusOps.VoiceWorker.Models;
using NexusOps.VoiceWorker.Security;
using NexusOps.VoiceWorker.Services;

namespace NexusOps.VoiceWorker.Controllers;

[ApiController]
[Route("voice/calls")]
[Authorize(Roles = "Administrator,Manager")]
[EnableRateLimiting("voice")]
public sealed class VoiceCallsController(IVoiceCallService service, IVoiceRequestAuthorizer authorizer) : ControllerBase
{
    [HttpPost("start")]
    public async Task<ActionResult<StartVoiceCallResponse>> Start(StartVoiceCallRequest request, CancellationToken cancellationToken)
    {
        if (!await authorizer.CanAccessCallAsync(request.VoiceCallSessionId, cancellationToken)) return Forbid();
        return Accepted(await service.StartAsync(request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VoiceCallSession>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!await authorizer.CanAccessCallAsync(id, cancellationToken)) return Forbid();
        var session = await service.GetAsync(id, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<VoiceCallSession>> Complete(
        Guid id, CompleteVoiceCallRequest request, CancellationToken cancellationToken)
    {
        if (!await authorizer.CanAccessCallAsync(id, cancellationToken)) return Forbid();
        return ToResult(await service.SetStatusAsync(id, VoiceCallStatus.Completed, request.Summary, cancellationToken));
    }

    [HttpPost("{id:guid}/fail")]
    public async Task<ActionResult<VoiceCallSession>> Fail(
        Guid id, FailVoiceCallRequest request, CancellationToken cancellationToken)
    {
        if (!await authorizer.CanAccessCallAsync(id, cancellationToken)) return Forbid();
        return ToResult(await service.SetStatusAsync(id, VoiceCallStatus.Failed, request.Reason, cancellationToken));
    }

    [HttpPost("{id:guid}/outcome")]
    public async Task<ActionResult<VoiceCallSession>> Outcome(
        Guid id, VoiceCallOutcomeRequest request, CancellationToken cancellationToken)
    {
        if (!await authorizer.CanAccessCallAsync(id, cancellationToken)) return Forbid();
        return ToResult(await service.SetOutcomeAsync(id, request, cancellationToken));
    }

    private ActionResult<VoiceCallSession> ToResult(VoiceCallSession? session) =>
        session is null ? NotFound() : Ok(session);
}
