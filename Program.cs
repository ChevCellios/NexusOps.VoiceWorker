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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Threading.RateLimiting;
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
    builder.Configuration.AddUserSecrets<VoiceCallService>(optional: true);

builder.Services.AddControllers();
builder.Services.AddRazorPages()
    .AddApplicationPart(typeof(NexusOps.Web.Pages.IndexModel).Assembly);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<VoiceWorkerHealthCheck>("voice_worker");
builder.Services.AddOptions<TwilioOptions>().BindConfiguration(TwilioOptions.SectionName);
builder.Services.AddOptions<OpenAIRealtimeOptions>().BindConfiguration(OpenAIRealtimeOptions.SectionName);
builder.Services.AddHttpClient<TwilioVoiceProvider>();
builder.Services.AddHttpClient<BrowserRealtimeSessionService>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddOptions<SupabaseAuthOptions>().BindConfiguration(SupabaseAuthOptions.SectionName);
builder.Services.AddOptions<VoiceMediaSecurityOptions>().BindConfiguration(VoiceMediaSecurityOptions.SectionName);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // Railway terminates TLS at a dynamic edge proxy. Only deploy the container behind that trusted edge.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "NexusOps.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var issuedAt = context.Properties.IssuedUtc;
            if (issuedAt is not null && DateTimeOffset.UtcNow - issuedAt.Value < TimeSpan.FromMinutes(5)) return;

            var userIdValue = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var roleValue = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var roleStore = context.HttpContext.RequestServices.GetRequiredService<IUserRoleStore>();
            var currentRole = await roleStore.FindRoleAsync(userId, context.HttpContext.RequestAborted);
            if (currentRole is null || !string.Equals(currentRole.Value.ToString(), roleValue, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            context.Properties.IssuedUtc = DateTimeOffset.UtcNow;
            context.ShouldRenew = true;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("voice", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("realtime", limiter =>
    {
        limiter.PermitLimit = 3;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddHttpClient<SupabaseSignInService>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    });
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(NexusOpsTelemetry.ServiceName))
    .WithTracing(tracing =>
    {
        tracing.AddSource(NexusOpsTelemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(NexusOpsTelemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            metrics.AddOtlpExporter();
    });
builder.Services.AddOptions<VoiceCallQueueOptions>().BindConfiguration(VoiceCallQueueOptions.SectionName);
var persistenceProvider = builder.Configuration["Persistence:Provider"];
var connectionString = builder.Configuration.GetConnectionString("NexusOps");
var tenantId = builder.Configuration["NexusOps:TenantId"];
var useInMemoryPersistence = string.Equals(persistenceProvider, "InMemory", StringComparison.OrdinalIgnoreCase)
    || string.IsNullOrWhiteSpace(connectionString);
if (useInMemoryPersistence)
{
    builder.Services.AddSingleton<IVoiceCallRepository, InMemoryVoiceCallRepository>();
    builder.Services.AddSingleton<IVoiceTranscriptRepository, InMemoryVoiceTranscriptRepository>();
    builder.Services.AddSingleton<IVoiceCallQueue, DisabledVoiceCallQueue>();
}
else
{
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString!));
    builder.Services.AddHostedService<DatabaseMigrationService>();
    builder.Services.AddSingleton<IVoiceCallRepository, PostgresVoiceCallRepository>();
    builder.Services.AddSingleton<IVoiceTranscriptRepository, PostgresVoiceTranscriptRepository>();
    builder.Services.AddSingleton<IVoiceCallQueue, PostgresVoiceCallQueue>();
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
builder.Services.AddScoped<IVoiceRequestAuthorizer, VoiceRequestAuthorizer>();
builder.Services.AddSingleton<ITwilioRequestValidator, TwilioRequestValidator>();
builder.Services.AddSingleton<NexusOps.Web.Services.IDemoNotificationStore, NexusOps.Web.Services.DemoNotificationStore>();
builder.Services.AddScoped<IVoiceCallService, VoiceCallService>();
builder.Services.AddSingleton<VoiceMediaWebSocketHandler>();
builder.Services.AddHostedService<QueuedVoiceCallWorker>();

var app = builder.Build();
ValidateProductionConfiguration(app.Configuration, app.Environment);

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-ID";
    var supplied = context.Request.Headers[headerName].ToString();
    var correlationId = !string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 128
        ? supplied
        : context.TraceIdentifier;
    context.TraceIdentifier = correlationId;
    context.Response.Headers[headerName] = correlationId;
    System.Diagnostics.Activity.Current?.SetTag("correlation.id", correlationId);
    using (context.RequestServices.GetRequiredService<ILoggerFactory>()
               .CreateLogger("NexusOps.Correlation")
               .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        await next();
});
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), geolocation=(), payment=(), usb=()");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self' https://api.openai.com wss://api.openai.com");
    await next();
});
app.UseWebSockets();
app.UseRateLimiter();
app.UseAuthentication();
var useLocalDevelopmentIdentity = string.Equals(requestedEnvironment, Environments.Development, StringComparison.OrdinalIgnoreCase) &&
                                  !app.Configuration.GetValue<bool>("SupabaseAuth:Enabled");
if (useLocalDevelopmentIdentity)
{
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var claims = new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Local Developer"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Administrator")
            };
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "LocalDevelopment"));
        }
        await next();
    });
}
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var isCommandCenter = context.Request.Path == "/index.html" ||
                          context.Request.Path.StartsWithSegments("/command-center");
    var isRealtimeTest = context.Request.Path == "/realtime-test.html";
    if (isCommandCenter && !context.User.IsInRole("Administrator"))
    {
        var reason = context.User.Identity?.IsAuthenticated == true ? "role" : "signin";
        context.Response.Redirect($"/Voice/Access?reason={reason}");
        return;
    }
    if (isRealtimeTest && !context.User.IsInRole("Administrator") && !context.User.IsInRole("Manager"))
    {
        context.Response.StatusCode = context.User.Identity?.IsAuthenticated == true
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});
app.UseStaticFiles();
var supabaseAuth = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseAuthOptions>>().Value;
if (supabaseAuth.Enabled && supabaseAuth.RequireAuthenticatedUsers)
{
    app.Use(async (context, next) =>
    {
        var isAccountRoute = context.Request.Path.StartsWithSegments("/Account");
        var isVoiceAccessRoute = context.Request.Path.StartsWithSegments("/Voice/Access");
        var isRazorPageRequest = !Path.HasExtension(context.Request.Path) &&
                                 !context.Request.Path.StartsWithSegments("/voice") &&
                                 !context.Request.Path.StartsWithSegments("/health") &&
                                 !context.Request.Path.StartsWithSegments("/status") &&
                                 !context.Request.Path.StartsWithSegments("/command-center");
        if (isRazorPageRequest && !isAccountRoute && !isVoiceAccessRoute &&
            context.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Uri.EscapeDataString($"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
            context.Response.Redirect($"/Account/Login?ReturnUrl={returnUrl}");
            return;
        }

        var isDemoUser = context.User.IsInRole(NexusOpsRole.Demo.ToString());
        var isDemoRoute = context.Request.Path.StartsWithSegments("/Demo");
        if (isDemoUser && !isAccountRoute && !isDemoRoute && !isVoiceAccessRoute)
        {
            context.Response.Redirect("/Demo");
            return;
        }

        var isOperationsManager = context.User.IsInRole(NexusOpsRole.Administrator.ToString()) ||
                                  context.User.IsInRole(NexusOpsRole.Manager.ToString());
        var isTechnicianUpdatingStatus = context.User.IsInRole(NexusOpsRole.Technician.ToString()) &&
                                         context.Request.Path.StartsWithSegments("/WorkOrders/Details");
        if (isRazorPageRequest && !isAccountRoute && !isDemoRoute && HttpMethods.IsPost(context.Request.Method) &&
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
app.MapGet("/status", VoiceWorkerStatusPage.WriteAsync)
    .RequireAuthorization(policy => policy.RequireRole("Administrator"));
app.MapMethods("/health", [HttpMethods.Get, HttpMethods.Head], VoiceWorkerStatusPage.WriteHealthAsync)
    .DisableRateLimiting();
app.MapGet("/command-center", () => Results.Redirect("/index.html", permanent: false));
app.Map("/voice/media", async context =>
{
    var handler = context.RequestServices.GetRequiredService<VoiceMediaWebSocketHandler>();
    await handler.HandleAsync(context);
});

app.Run();

static void ValidateProductionConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
{
    if (environment.IsDevelopment() || environment.EnvironmentName == "LocalRuntime") return;
    var errors = new List<string>();
    if (!configuration.GetValue<bool>("SupabaseAuth:Enabled") ||
        !configuration.GetValue<bool>("SupabaseAuth:RequireAuthenticatedUsers"))
        errors.Add("Supabase authentication must be enabled and required.");
    if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("NexusOps")))
        errors.Add("ConnectionStrings:NexusOps is required.");
    if (!Guid.TryParse(configuration["NexusOps:TenantId"], out _))
        errors.Add("NexusOps:TenantId must be a valid UUID.");
    if (!configuration.GetValue("Twilio:ValidateSignatures", true))
        errors.Add("Twilio signature validation must remain enabled.");
    if (!IsConfiguredSecret(configuration["Twilio:AuthToken"]))
        errors.Add("Twilio:AuthToken is required.");
    if (!IsConfiguredSecret(configuration["OpenAI:ApiKey"]))
        errors.Add("OpenAI:ApiKey is required.");
    if (!Uri.TryCreate(configuration["Twilio:PublicBaseUrl"], UriKind.Absolute, out var publicUrl) || publicUrl.Scheme != Uri.UriSchemeHttps)
        errors.Add("Twilio:PublicBaseUrl must be an absolute HTTPS URL.");
    if (!Uri.TryCreate(configuration["Twilio:MediaStreamUrl"], UriKind.Absolute, out var mediaUrl) || mediaUrl.Scheme != "wss")
        errors.Add("Twilio:MediaStreamUrl must be an absolute WSS URL.");
    if (!Uri.TryCreate(configuration["SupabaseAuth:Url"], UriKind.Absolute, out var supabaseUrl) || supabaseUrl.Scheme != Uri.UriSchemeHttps ||
        !IsConfiguredSecret(configuration["SupabaseAuth:PublishableKey"]))
        errors.Add("Supabase URL and publishable key must be configured.");
    if (configuration["AllowedHosts"] is null or "" or "*")
        errors.Add("AllowedHosts must contain the production hostname.");
    if (errors.Count > 0)
        throw new InvalidOperationException("Unsafe production configuration: " + string.Join(" ", errors));
}

static bool IsConfiguredSecret(string? value) =>
    !string.IsNullOrWhiteSpace(value) && !value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

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
