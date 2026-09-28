# Contributing to Signal.NET

Thanks for your interest! This guide covers the local setup and the conventions the build enforces.

## Prerequisites

- The .NET SDK pinned in [`global.json`](global.json) (.NET 11 preview; C# 15 preview features such as unions are used)
- Docker, only for end-to-end testing against a real signal-cli-rest-api container

## Build and test

```bash
dotnet restore
dotnet build
dotnet test
```

The unit and integration tests need no container, and neither does CI.

For an end-to-end check, start the container with `docker compose up -d` and link a device (see the
[README](README.md#quick-start)). Put your number into user secrets rather than `appsettings.json`:

```bash
dotnet user-secrets --project samples/Signal.Sample.Bot set "Signal:Accounts:0" "+4915112345678"
dotnet run --project samples/Signal.Sample.Bot
```

## Conventions

- **Architecture:** respect the layering in [docs/architecture.md](docs/architecture.md). Domain has no dependencies; Application defines ports; Infrastructure implements them; Hosting composes.
- **Warnings are errors**, including **missing XML docs on public API** (CS1591). Document every public member you add.
- **Code style:** `.editorconfig` describes the style (file-scoped namespaces, braces, `_camelCase` fields).
- **Unions:** use them for closed sets of alternatives (see [docs/domain.md](docs/domain.md#c-15-unions-in-the-domain)).
- **Tests:** add or adjust tests for every behaviour change.
- **Docs:** update `docs/` and `CHANGELOG.md` when behaviour, configuration or public API changes.
- **Never commit account data:** phone numbers, `signal-cli-config/` or secrets. The `.gitignore` covers the usual locations.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Make focused commits with descriptive messages.
3. Open a pull request. CI builds and tests on Linux and Windows and packs the NuGet packages.
