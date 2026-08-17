# NexusOps.VoiceWorker

Compilable ASP.NET Core starter for the NexusOps voice-call boundary. It exposes the planned REST and WebSocket routes, reads and updates existing `voice_call_sessions` rows in PostgreSQL, but deliberately makes no Twilio or OpenAI network calls.

## Run

```powershell
dotnet run
```

Open the displayed local URL (for example `http://localhost:5227`) to see the development status page. `/health` returns the same readiness information as JSON. `/voice/media` is a WebSocket route and is not intended to be opened directly in a browser.

Configuration values in `appsettings.json` are placeholders. Store real secrets outside source control, for example with environment variables or a secret manager.

### Visual Studio User Secrets

Right-click the project and choose **Manage User Secrets**, then use:

```json
{
  "Persistence": {
    "Provider": "PostgreSql"
  },
  "ConnectionStrings": {
    "NexusOps": "Host=localhost;Port=5432;Database=nexusops;Username=YOUR_USER;Password=YOUR_PASSWORD"
  },
  "Twilio": {
    "AccountSid": "YOUR_REAL_ACCOUNT_SID",
    "AuthToken": "YOUR_REAL_AUTH_TOKEN",
    "FromPhoneNumber": "+385...",
    "PublicBaseUrl": "https://YOUR_PUBLIC_HOST",
    "MediaStreamUrl": "wss://YOUR_PUBLIC_HOST/voice/media"
  },
  "OpenAI": {
    "ApiKey": "YOUR_REAL_OPENAI_API_KEY"
  }
}
```

Never commit the generated secrets file. `/health` verifies PostgreSQL connectivity and only reports whether provider secrets are configured; it never returns secret values.

For this local workspace the service reads PostgreSQL configuration from `nexusops.connection.local.txt`. This plain-text file tolerates accidental line breaks and is excluded by `.gitignore`. Keep it local and never commit or share it. Visual Studio User Secrets and `appsettings.Local.json` are intentionally not loaded by the local runtime profile.

OpenAI and Twilio local secrets are read from `openai.key.local.txt` and `twilio.local.txt`. Both files are excluded by `.gitignore`; keep each value on its provided line and never share the files.

## Railway deployment

The included multi-stage `Dockerfile` publishes and runs the .NET 9 service. At runtime the app binds to `0.0.0.0:$PORT`, as required by Railway. Local secret files are excluded from the image through `.dockerignore`.

Add the keys from `railway.variables.example.txt` in the Railway service's Variables tab, using real secret values. After deployment, generate a public domain under Settings → Networking and set both Twilio public URLs to that domain. Set the Railway health-check path to `/health`.

Production/default configuration uses PostgreSQL. Set the connection string securely with:

```text
ConnectionStrings__NexusOps=Host=...;Database=...;Username=...;Password=...
```

The Development profile uses `InMemory` persistence so the service can start without a database. Override `Persistence__Provider=PostgreSql` to exercise the real repository locally.

## Integration seams

- `PostgresVoiceCallRepository` maps to the existing NexusOps `voice_call_sessions` schema.
- `TwilioVoiceProvider` creates outbound calls through the Twilio Calls API.
- Provider callbacks validate `X-Twilio-Signature`; configure the exact externally visible `Twilio:PublicBaseUrl`.
- `/voice/provider/answer` returns `<Connect><Stream>` TwiML for a bidirectional media stream.
- `OpenAIRealtimeClient` bridges Twilio Media Stream audio to OpenAI Realtime in both directions using G.711 μ-law, with server VAD and interruption handling.
- Final user and agent transcripts are appended to `voice_call_messages`; per-call advisory locking keeps `sequence_number` unique under concurrent events.
- Replace `DevelopmentVoiceRequestAuthorizer` with tenant permissions and Twilio webhook signature validation.
- Enable queue polling in `QueuedVoiceCallWorker`.
