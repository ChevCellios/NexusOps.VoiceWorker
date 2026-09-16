# Changelog

All notable changes are documented here. This project follows Semantic Versioning and uses prerelease identifiers while it is in beta.

## [Unreleased]

### Added

- Durable PostgreSQL voice-call queue with leased `SKIP LOCKED` claims, safe throttling retries, and dead-letter state.
- OpenTelemetry traces and metrics with request correlation IDs and optional OTLP export.
- Docker-backed PostgreSQL integration tests for queue migration, concurrency, tenant isolation, and retry state.

### Changed

- Upgraded the runtime, build images, CI, and test targets from .NET 9 to .NET 10 LTS.
- Added timeout and circuit-breaker protection to OpenAI and Supabase HTTP clients without retrying unsafe POST requests.

### Fixed

- Replaced obsolete Command Center and browser Realtime access-key prompts with role-protected cookie authentication.
- Revalidated active user roles during long-lived sessions and rejected unsafe return URLs.
- Limited browser Realtime request sizes and removed upstream response bodies from provider error logs.

## [0.2.0-beta.1] - 2026-09-15

### Added

- Repeatable PostgreSQL migrations with locking, checksums, and an applied-migration history.
- Application version in the health and administrator status endpoints.
- Staging configuration and a tag-driven GitHub release workflow.

### Changed

- The .NET SDK is pinned for reproducible local and CI builds.

[Unreleased]: https://github.com/ChevCellios/NexusOps.VoiceWorker/compare/v0.2.0-beta.1...HEAD
[0.2.0-beta.1]: https://github.com/ChevCellios/NexusOps.VoiceWorker/releases/tag/v0.2.0-beta.1
