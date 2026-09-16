# Upgrading NexusOps

## Beta release policy

NexusOps uses Semantic Versioning. Beta releases use tags such as `v0.2.0-beta.1`. Back up PostgreSQL before every upgrade and deploy to staging before production.

## Upgrade from an existing installation

This version requires the .NET 10 SDK/runtime and applies `005_voice_call_queue.sql`. PostgreSQL deployments should back up the database before the first start and verify that the application database role can create and update `voice_call_queue`.

1. Back up PostgreSQL or create a Supabase point-in-time recovery point.
2. Deploy the candidate tag to a staging service with its own database.
3. Verify `GET /health`, sign-in, work orders, inventory, and a mock voice call.
4. Deploy the same immutable tag to production.
5. Confirm that `/health` returns the expected `version`.

At startup, the application applies files from `Database/Migrations` in filename order. Each migration runs once in a transaction. Applied filenames and SHA-256 checksums are stored in `nexusops_schema_migrations`. Never edit an applied migration; add a new numbered file instead.

Set `DatabaseMigrations__Enabled=false` only when migrations are run by a separate release job. Startup fails when a migration fails or when an applied file's checksum changes.

The legacy scripts in `NexusOps.Web/Database` bootstrap the current schema and include optional or environment-specific operations. They are not executed automatically. Existing deployments should keep their current schema and use `Database/Migrations` for all future changes.

## Creating a beta release

1. Update `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`.
2. Move completed entries from `Unreleased` into the new version in `CHANGELOG.md`.
3. Run the release build, tests, NuGet audit, and Docker build.
4. Merge to `main` and create a matching tag:

```powershell
git tag v0.2.0-beta.1
git push origin v0.2.0-beta.1
```

The release workflow rejects a mismatched tag, runs tests, publishes the application, and creates a prerelease on GitHub.

## Rollback

Use Railway's deployment history to redeploy the previous known-good image or commit. Database migrations are forward-only: restore the pre-upgrade backup if a schema rollback is required. Never delete rows from `nexusops_schema_migrations` to simulate a rollback.
