using NexusOps.VoiceWorker.Models;
using NexusOps.VoiceWorker.Persistence;
using NexusOps.VoiceWorker.Providers;
using NexusOps.VoiceWorker.Services;
using Xunit;

namespace NexusOps.Web.Tests;

public sealed class VoiceCallServiceTests
{
    [Fact]
    public async Task StartAsync_ConcurrentRequests_StartProviderOnlyOnce()
    {
        var repository = new InMemoryVoiceCallRepository();
        var session = CreateSession();
        await repository.UpsertAsync(session, CancellationToken.None);
        var provider = new BlockingVoiceProvider();
        var service = new VoiceCallService(repository, provider);
        var request = new StartVoiceCallRequest(session.Id, session.AgentTaskId);

        var first = service.StartAsync(request, CancellationToken.None);
        await provider.Started.Task.WaitAsync(CancellationToken.None);
        var second = service.StartAsync(request, CancellationToken.None);
        provider.Release.TrySetResult();

        await first;
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal(1, provider.StartCount);
    }

    [Fact]
    public async Task ApplyProviderStatusAsync_RejectsMismatchedProviderCallId()
    {
        var repository = new InMemoryVoiceCallRepository();
        var session = CreateSession() with
        {
            Status = VoiceCallStatus.Initiating,
            ProviderCallId = "CA-correct"
        };
        await repository.UpsertAsync(session, CancellationToken.None);
        var service = new VoiceCallService(repository, new BlockingVoiceProvider());

        var result = await service.ApplyProviderStatusAsync(
            new ProviderStatusRequest("CA-wrong", "completed", session.Id),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(VoiceCallStatus.Initiating,
            (await repository.GetAsync(session.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task ApplyProviderStatusAsync_DoesNotRegressTerminalStatus()
    {
        var repository = new InMemoryVoiceCallRepository();
        var session = CreateSession() with
        {
            Status = VoiceCallStatus.InProgress,
            ProviderCallId = "CA123"
        };
        await repository.UpsertAsync(session, CancellationToken.None);
        var service = new VoiceCallService(repository, new BlockingVoiceProvider());

        await service.ApplyProviderStatusAsync(
            new ProviderStatusRequest("CA123", "completed", session.Id),
            CancellationToken.None);
        var result = await service.ApplyProviderStatusAsync(
            new ProviderStatusRequest("CA123", "ringing", session.Id),
            CancellationToken.None);

        Assert.Equal(VoiceCallStatus.Completed, result!.Status);
    }

    private static VoiceCallSession CreateSession()
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            "outbound", VoiceCallStatus.Queued, "twilio", null, "+10000000000",
            "+38510000000", "Test", "hr", null, null, false, false, false,
            null, null, null, null, null, null, now, now);
    }

    private sealed class BlockingVoiceProvider : IVoiceProvider
    {
        private int _startCount;

        public string Name => "twilio";
        public int StartCount => _startCount;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string?> StartCallAsync(VoiceCallSession session, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return "CA123";
        }

        public string GetMediaStreamUrl(Guid voiceCallSessionId) =>
            $"wss://example.test/voice/media?voiceCallSessionId={voiceCallSessionId:D}";
    }
}
