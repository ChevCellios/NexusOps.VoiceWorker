using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NexusOps.VoiceWorker.Diagnostics;

public static class NexusOpsTelemetry
{
    public const string ServiceName = "NexusOps.VoiceWorker";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);
    public static readonly Counter<long> QueueCompleted = Meter.CreateCounter<long>("nexusops.voice.queue.completed");
    public static readonly Counter<long> QueueRetried = Meter.CreateCounter<long>("nexusops.voice.queue.retried");
    public static readonly Counter<long> QueueDeadLettered = Meter.CreateCounter<long>("nexusops.voice.queue.dead_lettered");
}
