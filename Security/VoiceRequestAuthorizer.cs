using System.Security.Claims;
using NexusOps.VoiceWorker.Persistence;

namespace NexusOps.VoiceWorker.Security;

public interface IVoiceRequestAuthorizer
{
    Task<bool> CanAccessCallAsync(Guid voiceCallSessionId, CancellationToken cancellationToken);
}

public sealed class VoiceRequestAuthorizer(
    IHttpContextAccessor httpContextAccessor,
    IVoiceCallRepository calls,
    IConfiguration configuration) : IVoiceRequestAuthorizer
{
    public async Task<bool> CanAccessCallAsync(Guid voiceCallSessionId, CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (!user.IsInRole("Administrator") && !user.IsInRole("Manager")) return false;

        var configuredTenant = configuration["NexusOps:TenantId"];
        if (!Guid.TryParse(configuredTenant, out var tenantId)) return false;
        var session = await calls.GetAsync(voiceCallSessionId, cancellationToken);
        return session is not null && session.TenantId == tenantId &&
               user.FindFirstValue(ClaimTypes.NameIdentifier) is not null;
    }
}
