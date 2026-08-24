# NexusOps.VoiceWorker

[![CI](https://github.com/ChevCellios/NexusOps.VoiceWorker/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/ChevCellios/NexusOps.VoiceWorker/actions/workflows/ci.yml)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)
![Railway](https://img.shields.io/badge/deploy-Railway-0B0D0E)

## CI/CD

GitHub Actions workflow `.github/workflows/ci.yml` restores, builds, runs unit tests, and verifies the Docker image on pushes and pull requests targeting `main`. It can also be started manually from the **Actions** tab.

Production deployment uses Railway's existing GitHub autodeploy integration. Enable **Wait for CI** in the Railway service settings so Railway deploys a commit only after the GitHub workflow succeeds. No Railway token is required in GitHub for this setup.

Compilable ASP.NET Core starter for the NexusOps voice-call boundary. It exposes the planned REST and WebSocket routes, reads and updates existing `voice_call_sessions` rows in PostgreSQL, and can initiate a Twilio call when the provider is configured. The OpenAI Realtime integration remains a controlled starter implementation.

## NexusOps operations application

The same Railway service also hosts the NexusOps operations interface:

- **Dashboard** — current operational overview and priority work orders.
- **Work orders** — create an intervention, set a due date and priority, assign it, update its status and review its activity timeline.
- **Assets** — register equipment, update its operational status and location, and view linked work orders.
- **Reports** — filter work orders by status, priority and date; highlight overdue and near-due work; export the current result set as a UTF-8 CSV file for Excel.
- **Voice Command Center** — available at `/command-center` for controlled voice-call testing and monitoring.

v0.1 also includes finance, team presence, inventory movements and transfers, customer orders with automatic work-order creation, labor cost tracking, and a cost-free mock Notification Center.

The web interface and voice endpoints deliberately share one deployment, domain and PostgreSQL configuration. Application data is separated by the configured `NexusOps:TenantId`.

### Demo corporation and operations data

The optional **Adria Dynamics d.o.o.** dataset adds three business units, employees, warehouses, inventory, fleet assets, finance records, a loan, public tenders, presence and time-work examples. Run the SQL scripts in this order in the Supabase SQL Editor:

1. `001_operations_schema.sql`
2. `002_user_access.sql`
3. `003_finance_and_tenders.sql`
4. `004_corporate_operations.sql`
5. `006_finance_schema_compatibility.sql` when an older `loans` table already exists
6. `005_adria_dynamics_demo_seed.sql`
7. `007_customer_order_automation.sql`
8. `008_work_order_labor.sql`
9. `009_rls_baseline.sql`
10. `010_public_demo_access.sql` if you want to enable the restricted public demo user
11. `011_employee_auth_link.sql` for each existing employee who should use **Moji radni nalozi**

The seed script is repeatable. It never contains real people or financial data.

### v0.1 demo flow

1. Create a customer order in **Narudžbe**.
2. Verify its automatically created work order in **Radni nalozi**.
3. In **Skladište**, issue material and select that work order.
4. On the work-order detail, record labor time and hourly rate.
5. Review material and labor costs on the same detail page.
6. In **Obavijesti**, simulate a notification. It is mock-only and never sends a real message.

### Security baseline

`009_rls_baseline.sql` revokes direct `anon` and `authenticated` access to NexusOps business tables and enables RLS. The v0.1 UI accesses PostgreSQL through the Railway backend, which applies tenant and role checks. Never expose database passwords, service keys, Twilio tokens or OpenAI keys in browser code or source control.

### Supabase Auth and roles

NexusOps supports email/password sign-in through Supabase Auth. Authentication is deliberately off by default, so adding the feature never locks an existing deployment. To activate it:

1. Run [`NexusOps.Web/Database/002_user_access.sql`](NexusOps.Web/Database/002_user_access.sql) in the Supabase SQL Editor.
2. Create the first user in **Supabase Dashboard → Authentication → Users** and copy that user's UUID.
3. Insert a role mapping using the example at the bottom of the SQL file. The first user should normally be `Administrator`.
4. Add these Railway variables:

```text
SupabaseAuth__Enabled=true
SupabaseAuth__RequireAuthenticatedUsers=true
SupabaseAuth__Url=https://YOUR_PROJECT.supabase.co
SupabaseAuth__PublishableKey=YOUR_SUPABASE_PUBLISHABLE_OR_ANON_KEY
```

`PublishableKey` is intended for client-side identification and is not a service-role key. Never add a Supabase `service_role` key to Railway or source control. Once enabled, users without a `nexusops_user_roles` row cannot sign in. `Viewer` users can inspect data but cannot submit changes. `Technician` users can update a work order's status. `Manager` and `Administrator` users can create and edit operational records. NexusOps records the signed-in e-mail in work-order activity events.

### Public demo user

The public demo account has no access to the dashboard, customers, employees, finance, inventory or real telephony. It can only open `/Demo` and simulate a Twilio call for the fictional work order `RN-DEMO-001`.

```text
E-mail: demo@nexusops.app
Password: NexusOps!Demo26
```

To enable it, run `NexusOps.Web/Database/010_public_demo_access.sql`, create that confirmed user under **Supabase Dashboard → Authentication → Users**, then run the final mapping query from the same SQL file after replacing `YOUR_TENANT_UUID`. This public password is intentionally documented and must never be reused for an administrator account. It is not a Railway variable.

### Technician work view

`Moji radni nalozi` is available to a `Technician` account. It shows only work orders assigned to that employee and enables personal start/end work tracking. For existing employees, run `NexusOps.Web/Database/011_employee_auth_link.sql` once after replacing its tenant UUID. The script links every `employees.auth_user_id` to the matching Supabase Auth user by e-mail and displays a verification list. New users created through **Korisnici** are linked automatically when their e-mail already exists in `employees`.

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
