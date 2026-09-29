# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **Release process (roadmap 0.2):**
  - A tag-driven release workflow (NuGet via Trusted Publishing, then a GitHub Release).
  - MinVer versioning from git tags.
  - Public API tracking with `PublicAPI.*.txt`.
  - Package validation.
  - Coverage summaries in CI.
  - A DocFX documentation site on GitHub Pages.
  - The `Signal.NET.Templates` package with a `dotnet new signalbot` template.

- **Layers:**
  - `Signal.Domain`: value objects, the `IncomingEnvelope` aggregate, `OutgoingMessage` builder, entities and domain events.
  - `Signal.Application`: ports, options with validation, the message pipeline with built-in middleware, and domain event dispatching.
  - `Signal.Infrastructure`: REST adapters for every endpoint group, a source-generated JSON contract, the resilience pipeline, and polling and WebSocket receivers.
  - `Signal.Hosting`: `AddSignal()`, `ISignalBuilder`, the partitioned `SignalHostedService` and a health check.
- **Text command system:** module and class commands, lambda commands, typed argument binding, flags, preconditions, cooldowns and generated help.
- **Execution modes:** support for all four container modes (`normal`, `native`, `json-rpc`, `json-rpc-native`), with startup mode verification.
- **C# 15 unions:** `Recipient`, `EnvelopeContent`, `CommandResult`, `ArgumentBindingResult`.
- **Documentation:** full XML documentation (enforced by the build) and the `docs/` guides.
- **Repository:** sample bot, Docker Compose file, CI workflow for GitHub Actions.
