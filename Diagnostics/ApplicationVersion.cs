using System.Reflection;

namespace NexusOps.VoiceWorker.Diagnostics;

public static class ApplicationVersion
{
    public static string Current { get; } =
        typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
        ?? "unknown";
}
