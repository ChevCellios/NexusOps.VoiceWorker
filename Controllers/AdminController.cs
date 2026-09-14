using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusOps.VoiceWorker.Persistence;

namespace NexusOps.VoiceWorker.Controllers;

[ApiController]
[Route("admin/api")]
[Authorize(Roles = "Administrator")]
public sealed class AdminController(
    IVoiceCallRepository calls,
    IVoiceTranscriptRepository transcripts,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("calls")]
    public async Task<IActionResult> Calls(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(configuration["NexusOps:TenantId"], out var tenantId)) return Problem("Tenant is not configured.");
        return Ok((await calls.ListAsync(200, cancellationToken)).Where(call => call.TenantId == tenantId).Take(100));
    }

    [HttpGet("calls/{id:guid}/transcript")]
    public async Task<IActionResult> Transcript(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(configuration["NexusOps:TenantId"], out var tenantId)) return Problem("Tenant is not configured.");
        var call = await calls.GetAsync(id, cancellationToken);
        if (call is null || call.TenantId != tenantId) return NotFound();
        return Ok(await transcripts.ListAsync(id, cancellationToken));
    }
}
