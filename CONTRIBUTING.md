# Contributing to Signal.NET

Thanks for your interest! This guide covers the local setup and the conventions the build enforces.

## Prerequisites

- The .NET SDK pinned in [`global.json`](https://github.com/Nacorpio/Signal.NET/blob/main/global.json) (.NET 11 preview; C# 15 preview features such as unions are used)
- Docker, only for end-to-end testing against a real signal-cli-rest-api container

## Build and test

```bash
dotnet restore
dotnet build
dotnet test
```

The unit and integration tests need no container, and neither does CI.

For an end-to-end check, start the container with `docker compose up -d` and link a device (see the
[README](https://github.com/Nacorpio/Signal.NET#quick-start)). Put your number into user secrets rather than `appsettings.json`:

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

## Public API changes

Each library has `PublicAPI.Shipped.txt` (APIs in released versions) and `PublicAPI.Unshipped.txt` (APIs
added since). The build fails (RS0016/RS0017) when the public surface and these files disagree:

- **Adding API:** run the analyzer's code fix (the lightbulb in the IDE), or fix everything at once:

  ```bash
  dotnet format analyzers src/Signal.Domain/Signal.Domain.csproj --diagnostics RS0016 --severity info
  ```

- **Removing or changing shipped API** is a breaking change. Discuss it in an issue first.

The diff of these files is the API review of a pull request.

## Documentation site

```bash
dotnet tool restore
dotnet docfx docfx.json --serve --port 8090   # 8080 is taken by signal-cli-rest-api
```

The site is published to GitHub Pages by `.github/workflows/docs.yml` on every push to `main`.

## Templates

Test changes to `templates/Signal.NET.Templates` against locally packed libraries:

```bash
dotnet pack -c Release -o ./feed
dotnet new install ./feed/Signal.NET.Templates.<version>.nupkg
dotnet new signalbot -n TestBot -o ../TestBot   # outside the repo, then add ./feed as a NuGet source
```

## Releasing (maintainers)

Versions come from git tags ([MinVer](https://github.com/adamralph/minver)). Untagged builds are
`0.2.0-preview.0.<height>`.

1. In `CHANGELOG.md`, move the *Unreleased* entries under `## [x.y.z] - YYYY-MM-DD`.
2. Move the contents of every `PublicAPI.Unshipped.txt` into the matching `PublicAPI.Shipped.txt`.
3. After the first release, set `PackageValidationBaselineVersion` in `src/Directory.Build.props` to the previous version.
4. Commit, then tag and push, e.g. `git tag v0.2.0 && git push origin v0.2.0`. Pre-releases use `v0.2.0-preview.1`.

`.github/workflows/release.yml` then builds, tests and packs, checks that the package versions match the tag,
publishes to nuget.org, and creates the GitHub Release with the changelog section.

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).
The workflow exchanges a GitHub OIDC token for an API key that is valid for one hour, so no long-lived key is
stored. One-time setup:

1. **nuget.org** → your username → **Trusted Publishing** → add a policy: Repository Owner `Nacorpio`,
   Repository `Signal.NET`, Workflow File `release.yml`, Environment `nuget`. Under **Select Scopes**, allow
   *Push new packages* and *Push new package versions* with the glob pattern `Signal.*`. The first release
   creates the package IDs, and the glob stops the policy from applying to your other packages.
2. **GitHub** → Settings → **Environments** → create `nuget`. Add the environment secret `NUGET_USER`
   (your nuget.org profile name, not your email). Optionally add yourself as a required reviewer, so every
   release waits for approval before publishing.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Make focused commits with descriptive messages.
3. Open a pull request. CI builds and tests on Linux and Windows and packs the NuGet packages.
