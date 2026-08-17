using NexusOps.VoiceWorker.Persistence;
using NexusOps.VoiceWorker.Providers;
using NexusOps.VoiceWorker.Providers.Twilio;
using NexusOps.VoiceWorker.Realtime;
using NexusOps.VoiceWorker.Realtime.OpenAI;
using NexusOps.VoiceWorker.Security;
using NexusOps.VoiceWorker.Services;
using NexusOps.VoiceWorker.WebSockets;
using NexusOps.VoiceWorker.Workers;
using Npgsql;
using NexusOps.VoiceWorker.Diagnostics;

var requestedEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environments.Production;
var effectiveEnvironment = string.Equals(requestedEnvironment, Environments.Development, StringComparison.OrdinalIgnoreCase)
    ? "LocalRuntime"
    : requestedEnvironment;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = effectiveEnvironment
});
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(railwayPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
LoadLocalConnectionString(builder.Configuration, builder.Environment.ContentRootPath);
LoadLocalProviderSecrets(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<VoiceWorkerHealthCheck>("voice_worker");
builder.Services.AddOptions<TwilioOptions>().BindConfiguration(TwilioOptions.SectionName);
builder.Services.AddOptions<OpenAIRealtimeOptions>().BindConfiguration(OpenAIRealtimeOptions.SectionName);
builder.Services.AddHttpClient<TwilioVoiceProvider>();
var persistenceProvider = builder.Configuration["Persistence:Provider"];
var connectionString = builder.Configuration.GetConnectionString("NexusOps");
var useInMemoryPersistence = string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrWhiteSpace(connectionString);
if (useInMemoryPersistence)
{
    builder.Services.AddSingleton<IVoiceCallRepository, InMemoryVoiceCallRepository>();
    builder.Services.AddSingleton<IVoiceTranscriptRepository, InMemoryVoiceTranscriptRepository>();
}
else
{
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString!));
    builder.Services.AddSingleton<IVoiceCallRepository, PostgresVoiceCallRepository>();
    builder.Services.AddSingleton<IVoiceTranscriptRepository, PostgresVoiceTranscriptRepository>();
}
builder.Services.AddTransient<IVoiceProvider>(services => services.GetRequiredService<TwilioVoiceProvider>());
builder.Services.AddSingleton<IRealtimeClient, OpenAIRealtimeClient>();
builder.Services.AddSingleton<IVoiceRequestAuthorizer, DevelopmentVoiceRequestAuthorizer>();
builder.Services.AddSingleton<ITwilioRequestValidator, TwilioRequestValidator>();
builder.Services.AddScoped<IVoiceCallService, VoiceCallService>();
builder.Services.AddSingleton<VoiceMediaWebSocketHandler>();
builder.Services.AddHostedService<QueuedVoiceCallWorker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseWebSockets();
app.MapControllers();
app.MapGet("/", VoiceWorkerStatusPage.WriteAsync);
app.MapGet("/health", VoiceWorkerStatusPage.WriteHealthAsync);
app.Map("/voice/media", async context =>
{
    var handler = context.RequestServices.GetRequiredService<VoiceMediaWebSocketHandler>();
    await handler.HandleAsync(context);
});

app.Run();

static void LoadLocalConnectionString(IConfiguration configuration, string contentRootPath)
{
    var localPath = Path.Combine(contentRootPath, "nexusops.connection.local.txt");
    if (!File.Exists(localPath)) return;

    var connectionString = File.ReadAllText(localPath)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", string.Empty, StringComparison.Ordinal)
        .Trim();
    if (connectionString.Length == 0) return;

    configuration["Persistence:Provider"] = "PostgreSql";
    configuration["ConnectionStrings:NexusOps"] = connectionString;
}

static void LoadLocalProviderSecrets(IConfiguration configuration, string contentRootPath)
{
    var openAiPath = Path.Combine(contentRootPath, "openai.key.local.txt");
    if (File.Exists(openAiPath))
    {
        var apiKey = File.ReadAllText(openAiPath)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (!string.IsNullOrWhiteSpace(apiKey)) configuration["OpenAI:ApiKey"] = apiKey;
    }

    var twilioPath = Path.Combine(contentRootPath, "twilio.local.txt");
    if (!File.Exists(twilioPath)) return;

    foreach (var rawLine in File.ReadLines(twilioPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var separator = line.IndexOf('=');
        if (separator <= 0) continue;
        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        if (value.Length > 0) configuration[$"Twilio:{key}"] = value;
    }
}

public partial class Program;
