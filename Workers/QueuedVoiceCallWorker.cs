namespace NexusOps.VoiceWorker.Workers;

public sealed class QueuedVoiceCallWorker(ILogger<QueuedVoiceCallWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Voice call worker started; queue polling is disabled in this starter.");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
