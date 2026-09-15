using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexusOps.VoiceWorker.Diagnostics;

public static class VoiceWorkerStatusPage
{
    public static async Task WriteAsync(HttpContext context)
    {
        var health = context.RequestServices.GetRequiredService<HealthCheckService>();
        var report = await health.CheckHealthAsync(context.RequestAborted);
        var check = report.Entries["voice_worker"];
        Func<string, string> encode = value => HtmlEncoder.Default.Encode(value);
        var persistence = encode(check.Data["persistence"].ToString()!);
        var version = encode(check.Data["version"].ToString()!);
        var database = (bool)check.Data["databaseConnected"] ? "Connected" : "Not connected";
        var twilio = (bool)check.Data["twilioConfigured"] ? "Configured" : "Not configured";
        var openAi = (bool)check.Data["openAiConfigured"] ? "Configured" : "Not configured";
        var model = encode(check.Data["realtimeModel"].ToString()!);

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync($$"""
            <!doctype html>
            <html lang="hr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>NexusOps Voice Worker</title>
              <style>
                body { font-family: system-ui,sans-serif; max-width: 850px; margin: 48px auto; padding: 0 20px; color: #172033; background: #f5f7fb; }
                main { background: white; padding: 32px; border-radius: 16px; box-shadow: 0 8px 30px #17203314; }
                .ok { color: #08783e; } .warn { color: #a15c00; }
                table { width: 100%; border-collapse: collapse; margin: 24px 0; }
                td { padding: 12px; border-bottom: 1px solid #e5e9f0; }
                td:first-child { font-weight: 600; }
                code { background: #eef1f6; padding: 3px 7px; border-radius: 5px; }
              </style>
            </head>
            <body><main>
              <h1>NexusOps Voice Worker</h1>
              <p class="ok">● Servis radi</p>
              <table>
                <tr><td>Version</td><td>{{version}}</td></tr>
                <tr><td>Persistence</td><td>{{persistence}}</td></tr>
                <tr><td>Database</td><td class="{{(database == "Connected" ? "ok" : "warn")}}">{{database}}</td></tr>
                <tr><td>Twilio</td><td class="{{(twilio == "Configured" ? "ok" : "warn")}}">{{twilio}}</td></tr>
                <tr><td>OpenAI</td><td class="{{(openAi == "Configured" ? "ok" : "warn")}}">{{openAi}}</td></tr>
                <tr><td>Realtime model</td><td>{{model}}</td></tr>
              </table>
              <h2>Važne rute</h2>
              <p><code>GET /health</code> strojno čitljiva provjera</p>
              <p><code>POST /voice/calls/start</code> pokretanje poziva</p>
              <p><code>POST /voice/provider/status</code> Twilio status webhook</p>
              <p><code>POST /voice/provider/answer</code> Twilio TwiML webhook</p>
              <p><code>WSS /voice/media</code> audio kanal — ne otvara se običnim preglednikom</p>
            </main></body></html>
            """, context.RequestAborted);
    }

    public static async Task WriteHealthAsync(HttpContext context)
    {
        var health = context.RequestServices.GetRequiredService<HealthCheckService>();
        var report = await health.CheckHealthAsync(context.RequestAborted);
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            version = ApplicationVersion.Current
        }, cancellationToken: context.RequestAborted);
    }
}
