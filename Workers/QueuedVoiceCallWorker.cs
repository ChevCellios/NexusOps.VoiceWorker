using System.Net;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Diagnostics;
using NexusOps.VoiceWorker.Models;
using NexusOps.VoiceWorker.Persistence;
using NexusOps.VoiceWorker.Services;

namespace NexusOps.VoiceWorker.Workers;

public sealed class VoiceCallQueueOptions
{
    public const string SectionName = "VoiceCallQueue";
    public bool Enabled { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 2;
    public int LeaseMinutes { get; init; } = 5;
    public int MaxAttempts { get; init; } = 4;
}

public sealed class QueuedVoiceCallWorker(
    IVoiceCallQueue queue,
    IVoiceCallRepository calls,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<VoiceCallQueueOptions> options,
    ILogger<QueuedVoiceCallWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !Guid.TryParse(configuration["NexusOps:TenantId"], out var tenantId))
        {
            logger.LogInformation("Voice call queue worker is disabled or no tenant is configured.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 1, 60));
        var lease = TimeSpan.FromMinutes(Math.Clamp(settings.LeaseMinutes, 1, 60));
        logger.LogInformation("Voice call queue worker started for tenant {TenantId}.", tenantId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queue.EnqueuePendingSessionsAsync(tenantId, stoppingToken);
                var queuedCall = await queue.ClaimAsync(tenantId, lease, stoppingToken);
                if (queuedCall is null)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }
                await ProcessAsync(queuedCall, settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Voice call queue polling failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(
        QueuedVoiceCall queuedCall,
        VoiceCallQueueOptions settings,
        CancellationToken cancellationToken)
    {
        using var activity = NexusOpsTelemetry.ActivitySource.StartActivity("voice.queue.process");
        activity?.SetTag("voice.session_id", queuedCall.SessionId);
        activity?.SetTag("queue.attempt", queuedCall.Attempt);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IVoiceCallService>();
            await service.StartAsync(
                new StartVoiceCallRequest(queuedCall.SessionId, queuedCall.AgentTaskId), cancellationToken);
            await queue.MarkCompletedAsync(queuedCall, cancellationToken);
            NexusOpsTelemetry.QueueCompleted.Add(1);
        }
        catch (HttpRequestException exception) when (
            exception.StatusCode == HttpStatusCode.TooManyRequests && queuedCall.Attempt < settings.MaxAttempts)
        {
            if (!await calls.TryRequeueAsync(queuedCall.SessionId, cancellationToken))
            {
                await queue.MarkDeadLetterAsync(queuedCall, exception.Message, cancellationToken);
                NexusOpsTelemetry.QueueDeadLettered.Add(1);
                return;
            }

            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, queuedCall.Attempt), 300));
            await queue.MarkRetryAsync(queuedCall, delay, exception.Message, cancellationToken);
            NexusOpsTelemetry.QueueRetried.Add(1);
            logger.LogWarning(
                "Twilio throttled voice session {SessionId}; retry {Attempt} scheduled in {Delay}.",
                queuedCall.SessionId, queuedCall.Attempt, delay);
        }
        catch (InvalidOperationException exception)
        {
            var session = await calls.GetAsync(queuedCall.SessionId, cancellationToken);
            if (session is not null && session.Status is not (VoiceCallStatus.Queued or VoiceCallStatus.Failed))
                await queue.MarkCompletedAsync(queuedCall, cancellationToken);
            else
            {
                await queue.MarkDeadLetterAsync(queuedCall, exception.Message, cancellationToken);
                NexusOpsTelemetry.QueueDeadLettered.Add(1);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await queue.MarkDeadLetterAsync(queuedCall, exception.Message, cancellationToken);
            NexusOpsTelemetry.QueueDeadLettered.Add(1);
            logger.LogError(exception, "Voice call session {SessionId} moved to dead-letter.", queuedCall.SessionId);
        }
    }
}
