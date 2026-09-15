using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NexusOps.VoiceWorker.Providers.Twilio;
using NexusOps.VoiceWorker.Realtime.OpenAI;
using Npgsql;

namespace NexusOps.VoiceWorker.Diagnostics;

public sealed class VoiceWorkerHealthCheck(
    IConfiguration configuration,
    IOptions<TwilioOptions> twilio,
    IOptions<OpenAIRealtimeOptions> openAi) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configuredPersistence = configuration["Persistence:Provider"] ?? "PostgreSql";
        var configuredConnectionString = configuration.GetConnectionString("NexusOps");
        var persistence = string.Equals(configuredPersistence, "InMemory", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuredConnectionString)
                ? "InMemory"
                : "PostgreSql";
        var twilioReady = IsSecretConfigured(twilio.Value.AccountSid) && IsSecretConfigured(twilio.Value.AuthToken);
        var openAiReady = IsSecretConfigured(openAi.Value.ApiKey);
        var databaseConnected = string.Equals(persistence, "InMemory", StringComparison.OrdinalIgnoreCase);
        string? databaseError = null;
        if (!databaseConnected)
        {
            try
            {
                await using var dataSource = NpgsqlDataSource.Create(configuredConnectionString!);
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                await using var command = new NpgsqlCommand("select 1", connection);
                await command.ExecuteScalarAsync(cancellationToken);
                databaseConnected = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                databaseError = exception.Message;
            }
        }

        var data = new Dictionary<string, object>
        {
            ["version"] = ApplicationVersion.Current,
            ["persistence"] = persistence,
            ["databaseConnected"] = databaseConnected,
            ["twilioConfigured"] = twilioReady,
            ["openAiConfigured"] = openAiReady,
            ["realtimeModel"] = openAi.Value.RealtimeModel
        };

        if (databaseError is not null) data["databaseError"] = databaseError;
        return databaseConnected
            ? HealthCheckResult.Healthy("Voice Worker is running and persistence is available.", data)
            : HealthCheckResult.Unhealthy("Voice Worker is running, but PostgreSQL is unavailable.", data: data);
    }

    private static bool IsSecretConfigured(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("YOUR_", StringComparison.Ordinal) &&
        !string.Equals(value, "CHANGE_ME", StringComparison.Ordinal);
}
