# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Contact the repository owner privately and include the affected component, reproduction steps, impact, and any suggested mitigation. Do not include real credentials, call recordings, transcripts, or customer data.

## Production requirements

NexusOps intentionally refuses to start in Production unless all of the following are configured:

- PostgreSQL connection string and a valid `NexusOps__TenantId`
- Supabase authentication enabled and required
- valid Supabase URL and publishable key
- Twilio signature validation enabled with a configured auth token
- HTTPS Twilio callback URL and WSS media-stream URL
- a concrete production hostname in `AllowedHosts`

Use Railway variables or another managed secret store. Never commit provider credentials or local `*.local.txt` files.

The application trusts one forwarded proxy hop because Railway terminates TLS at its edge. Do not expose the application container directly to the public internet without replacing this setting with explicit trusted proxy addresses or networks.

## Access model

- Voice-call management requires an authenticated Manager or Administrator and a matching tenant.
- Voice provider callbacks and media streams require a valid Twilio signature.
- Admin APIs require the Administrator role.
- Browser Realtime sessions require a Manager or Administrator plus the configured test access key.
- Demo users must never receive access to operational or voice-management endpoints.

## Release checks

Before deployment, require the build, tests, NuGet vulnerability audit, CodeQL analysis, Docker build, and Trivy image scan to pass. Review Dependabot pull requests and rotate any credential suspected of exposure.
