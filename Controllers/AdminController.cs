using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Persistence;

namespace NexusOps.VoiceWorker.Controllers;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public string AccessKey { get; init; } = string.Empty;
}

[ApiController]
[Route("admin/api")]
public sealed class AdminController(
    IVoiceCallRepository calls,
    IVoiceTranscriptRepository transcripts,
    IOptions<AdminOptions> options) : ControllerBase
{
    [HttpGet("calls")]
    public async Task<IActionResult> Calls(CancellationToken cancellationToken)
    {
        if (!Authorized()) return Unauthorized();
        return Ok(await calls.ListAsync(100, cancellationToken));
    }

    [HttpGet("calls/{id:guid}/transcript")]
    public async Task<IActionResult> Transcript(Guid id, CancellationToken cancellationToken)
    {
        if (!Authorized()) return Unauthorized();
        return Ok(await transcripts.ListAsync(id, cancellationToken));
    }

    private bool Authorized()
    {
        var expected = Encoding.UTF8.GetBytes(options.Value.AccessKey ?? string.Empty);
        var actual = Encoding.UTF8.GetBytes(Request.Headers["X-NexusOps-Admin-Key"].ToString());
        return expected.Length > 0 && expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
