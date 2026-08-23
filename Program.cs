using NexusOps.VoiceWorker.Persistence;
using NexusOps.VoiceWorker.Controllers;
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
using Microsoft.AspNetCore.Authentication.Cookies;
using NexusOps.Web.Security;

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
if (string.Equals(requestedEnvironment, Environments.Development, StringComparison.OrdinalIgnoreCase))
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddControllers();
builder.Services.AddRazorPages()
    .AddApplicationPart(typeof(NexusOps.Web.Pages.IndexModel).Assembly);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<VoiceWorkerHealthCheck>("voice_worker");
builder.Services.AddOptions<TwilioOptions>().BindConfiguration(TwilioOptions.SectionName);
builder.Services.AddOptions<OpenAIRealtimeOptions>().BindConfiguration(OpenAIRealtimeOptions.SectionName);
builder.Services.AddHttpClient<TwilioVoiceProvider>();
builder.Services.AddHttpClient<BrowserRealtimeSessionService>();
builder.Services.AddOptions<BrowserRealtimeTestOptions>().BindConfiguration(BrowserRealtimeTestOptions.SectionName);
builder.Services.AddOptions<AdminOptions>().BindConfiguration(AdminOptions.SectionName);
builder.Services.AddOptions<SupabaseAuthOptions>().BindConfiguration(SupabaseAuthOptions.SectionName);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "NexusOps.Auth";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<SupabaseSignInService>();
var persistenceProvider = builder.Configuration["Persistence:Provider"];
var connectionString = builder.Configuration.GetConnectionString("NexusOps");
var tenantId = builder.Configuration["NexusOps:TenantId"];
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
if (!string.IsNullOrWhiteSpace(connectionString) && Guid.TryParse(tenantId, out var parsedTenantId))
{
    builder.Services.AddSingleton<NexusOps.Web.Services.IOperationsStore>(services =>
        new NexusOps.Web.Services.PostgresOperationsStore(
            services.GetRequiredService<NpgsqlDataSource>(),
            parsedTenantId));
    builder.Services.AddSingleton<NexusOps.Web.Services.IFinanceStore>(services =>
        new NexusOps.Web.Services.PostgresFinanceStore(
            services.GetRequiredService<NpgsqlDataSource>(),
            parsedTenantId));
    builder.Services.AddSingleton<NexusOps.Web.Services.ITeamStore>(services => new NexusOps.Web.Services.PostgresTeamStore(services.GetRequiredService<NpgsqlDataSource>(), parsedTenantId));
    builder.Services.AddSingleton<NexusOps.Web.Services.IInventoryStore>(services => new NexusOps.Web.Services.PostgresInventoryStore(services.GetRequiredService<NpgsqlDataSource>(), parsedTenantId));
    builder.Services.AddSingleton<NexusOps.Web.Services.ICustomerOrderStore>(services => new NexusOps.Web.Services.PostgresCustomerOrderStore(services.GetRequiredService<NpgsqlDataSource>(), parsedTenantId));
    builder.Services.AddSingleton<NexusOps.Web.Services.ILaborStore>(services => new NexusOps.Web.Services.PostgresLaborStore(services.GetRequiredService<NpgsqlDataSource>(), parsedTenantId));
    builder.Services.AddSingleton<IUserRoleStore>(services =>
        new PostgresUserRoleStore(services.GetRequiredService<NpgsqlDataSource>(), parsedTenantId));
}
else
{
    builder.Services.AddSingleton<NexusOps.Web.Services.IOperationsStore,
        NexusOps.Web.Services.InMemoryOperationsStore>();
    builder.Services.AddSingleton<NexusOps.Web.Services.IFinanceStore,
        NexusOps.Web.Services.InMemoryFinanceStore>();
    builder.Services.AddSingleton<NexusOps.Web.Services.ITeamStore,
        NexusOps.Web.Services.InMemoryTeamStore>();
    builder.Services.AddSingleton<NexusOps.Web.Services.IInventoryStore,
        NexusOps.Web.Services.InMemoryInventoryStore>();
    builder.Services.AddSingleton<NexusOps.Web.Services.ICustomerOrderStore,
        NexusOps.Web.Services.InMemoryCustomerOrderStore>();
    builder.Services.AddSingleton<NexusOps.Web.Services.ILaborStore, NexusOps.Web.Services.InMemoryLaborStore>();
    builder.Services.AddSingleton<IUserRoleStore, UnconfiguredUserRoleStore>();
}
builder.Services.AddTransient<IVoiceProvider>(services => services.GetRequiredService<TwilioVoiceProvider>());
builder.Services.AddSingleton<IRealtimeClient, OpenAIRealtimeClient>();
builder.Services.AddSingleton<IVoiceRequestAuthorizer, DevelopmentVoiceRequestAuthorizer>();
builder.Services.AddSingleton<ITwilioRequestValidator, TwilioRequestValidator>();
builder.Services.AddSingleton<NexusOps.Web.Services.IDemoNotificationStore, NexusOps.Web.Services.DemoNotificationStore>();
builder.Services.AddScoped<IVoiceCallService, VoiceCallService>();
builder.Services.AddSingleton<VoiceMediaWebSocketHandler>();
builder.Services.AddHostedService<QueuedVoiceCallWorker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseWebSockets();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
var supabaseAuth = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseAuthOptions>>().Value;
if (supabaseAuth.Enabled && supabaseAuth.RequireAuthenticatedUsers)
{
    app.Use(async (context, next) =>
    {
        var isAccountRoute = context.Request.Path.StartsWithSegments("/Account");
        var isRazorPageRequest = !Path.HasExtension(context.Request.Path) &&
                                 !context.Request.Path.StartsWithSegments("/voice") &&
                                 !context.Request.Path.StartsWithSegments("/health") &&
                                 !context.Request.Path.StartsWithSegments("/status") &&
                                 !context.Request.Path.StartsWithSegments("/command-center");
        if (isRazorPageRequest && !isAccountRoute && context.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Uri.EscapeDataString($"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
            context.Response.Redirect($"/Account/Login?ReturnUrl={returnUrl}");
            return;
        }

        var isOperationsManager = context.User.IsInRole(NexusOpsRole.Administrator.ToString()) ||
                                  context.User.IsInRole(NexusOpsRole.Manager.ToString());
        var isTechnicianUpdatingStatus = context.User.IsInRole(NexusOpsRole.Technician.ToString()) &&
                                         context.Request.Path.StartsWithSegments("/WorkOrders/Details");
        if (isRazorPageRequest && !isAccountRoute && HttpMethods.IsPost(context.Request.Method) &&
            !isOperationsManager && !isTechnicianUpdatingStatus)
        {
            context.Response.Redirect("/Account/AccessDenied");
            return;
        }

        await next();
    });
}
app.MapControllers();
app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();
app.MapGet("/status", VoiceWorkerStatusPage.WriteAsync);
app.MapGet("/health", VoiceWorkerStatusPage.WriteHealthAsync);
app.MapGet("/command-center", () => Results.Redirect("/index.html", permanent: false));
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
