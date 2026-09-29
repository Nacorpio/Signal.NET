# Roadmap

This roadmap lists features that could follow from the current state of Signal.NET. It is a plan, not a
promise: priorities change with feedback, and items may move between milestones. Please discuss larger
items in an issue before starting work (see [CONTRIBUTING.md](CONTRIBUTING.md)).

**Legend.** Size is a rough effort estimate: **S** is a few hours, **M** a few days, **L** a week or more.
The layer column shows where the work mainly lands (**D**omain, **A**pplication, **I**nfrastructure,
**H**osting, **T**ooling).

---

## Where the project stands today (0.2.0-preview.2)

| Area | State |
|---|---|
| Architecture | Four layers (Domain → Application → Infrastructure → Hosting), DI throughout, `TryAdd` everywhere |
| Transport | REST adapters for 15 endpoint groups; HTTP polling (`normal`/`native`) and WebSocket (`json-rpc*`) receivers with backoff and reconnect |
| Commands | Modules, class and lambda commands; typed binding, flags, remainder; five preconditions and cooldowns; generated help |
| Processing | Middleware pipeline, domain events, conversation-partitioned concurrency |
| Language | .NET 11 **RC1** SDK; C# 15 unions (`Recipient`, `EnvelopeContent`, `CommandResult`, `ArgumentBindingResult`), C# 14 extension members |
| Quality | 118 tests, XML docs and public API tracking enforced by the build, CI with coverage on Linux and Windows |
| Delivery | Tag-driven releases with NuGet Trusted Publishing (approval-gated `nuget` environment), a [docs site](https://nacorpio.github.io/Signal.NET/), a `dotnet new signalbot` template. Published on nuget.org as [`Nacorpio.Signal.*`](https://www.nuget.org/packages/Nacorpio.Signal.Hosting) since [v0.2.0-preview.2](https://github.com/Nacorpio/Signal.NET/releases/tag/v0.2.0-preview.2). |

These known limitations shape the plan below:

1. **Long-running commands block their conversation partition.** The docs currently only advise against them.
2. **Reflection and `Expression.Compile`** in `CommandDescriptorFactory`, `ArgumentConverters` and `DomainEventDispatcher` rule out trimming and Native AOT.
3. **Unmodelled envelope content.** Sync, story, edit, remote-delete, sticker and call messages are dropped by `EnvelopeMapper`.
4. **State is in memory only.** Cooldowns and rate limits don't survive restarts and aren't shared between instances.
5. **Partial API coverage.** Registration, devices, stickers, username, account settings, remote delete and search are missing.
6. **Pre-release SDK dependency.** The project builds with the .NET 11 RC1 SDK and `LangVersion=preview`. It cannot declare a stable 1.0 before .NET 11 and C# 15 are generally available.

---

## Milestone 0.2: Release readiness

*Goal: installable from nuget.org, with a reproducible release process.*

**Status:** ✅ **complete.** The first nuget.org release is
[`0.2.0-preview.2`](https://github.com/Nacorpio/Signal.NET/releases/tag/v0.2.0-preview.2), under the owner-prefixed
IDs `Nacorpio.Signal.*`. It was verified from a clean machine: `dotnet add package Nacorpio.Signal.Hosting` resolves
the full chain, and the template installs from nuget.org and builds.

The earlier `v0.2.0-preview.1` exists only as a GitHub Release. Its `Signal.*` IDs fall under a prefix reserved by
another owner, so every push was rejected with *409 Conflict*, which `--skip-duplicate` reported as success.
Item 0.2.8 exists so that can't happen again.

| # | Feature | Layer | Size | Status |
|---|---|---|---|---|
| 0.2.1 | **Release workflow**: tag `v*` → build, test, pack, verify versions, push to nuget.org, GitHub Release with the `CHANGELOG.md` section | T | S | ✅ `.github/workflows/release.yml`, publishing through **NuGet Trusted Publishing** (no stored API key), gated by the `nuget` environment (tag rule `v*`, required reviewer) |
| 0.2.2 | **Versioning** from git tags | T | S | ✅ MinVer (`v` prefix, `preview.0` default) |
| 0.2.3 | **Public API tracking**, so breaking changes show up in review | T | S | ✅ `PublicAPI.*.txt` per library |
| 0.2.4 | **Package validation** | T | S | ✅ Baseline is the published `0.2.0-preview.2`; a removed public member fails `dotnet pack` (CP0002) |
| 0.2.5 | **Code coverage report** in CI | T | S | ✅ Job summary via ReportGenerator. A README coverage badge needs an external service (e.g. Codecov) and is deferred. |
| 0.2.6 | **Documentation site**: guides plus API reference on GitHub Pages | T | M | ✅ DocFX, `.github/workflows/docs.yml` |
| 0.2.7 | **`dotnet new signalbot` template** | T | M | ✅ `Nacorpio.Signal.Templates`, pinned to the matching package version |
| 0.2.8 | **Publish verification**: a rejected push fails the release, and the workflow confirms that each package is listed at the tagged version on nuget.org before announcing it | T | S | ✅ No `--skip-duplicate` (#11); `.github/scripts/wait-for-nuget.sh` polls the flat container index (what `dotnet restore` reads) for up to 45 minutes before the GitHub Release is created |

## Milestone 0.3: Complete API coverage

**Status:** all items are merged into `main` and not yet released (see the [changelog](CHANGELOG.md)).

*Goal: every signal-cli-rest-api capability reachable through a typed port. Check each endpoint against the
[Swagger spec](https://bbernhard.github.io/signal-cli-rest-api/) when implementing.*

| # | Feature | Layer | Size | Notes |
|---|---|---|---|---|
| 0.3.1 | **Registration and verification** | A/I | M | ✅ `IRegistrationService`: register (SMS/voice, captcha), verify (code plus registration-lock PIN), unregister. Enables bots without a primary phone. |
| 0.3.2 | **Device management** | A/I | S | ✅ `IDeviceService`: list (`LinkedDevice`), link by `sgnl://linkdevice` URI, remove. `IAccountService.GetLinkUriAsync` returns the raw link URI. |
| 0.3.3 | **Username management** | A/I | S | ✅ `IAccountService.SetUsernameAsync` (returns the assigned `alice.42` and share link) and `DeleteUsernameAsync` |
| 0.3.4 | **Account settings and PIN** | A/I | S | ✅ `UpdateSettingsAsync(AccountSettings)` (discoverability, number sharing), `SetPinAsync` / `RemovePinAsync` |
| 0.3.5 | **Remote delete** | D/A/I | S | ✅ `IMessageSender.RemoteDeleteAsync(account, recipient, sendResult.Timestamp)` (added without breaking existing implementations) |
| 0.3.6 | **Stickers** | D/A/I | M | ✅ `Sticker` value object (`packId:stickerId`, `IParsable`); `OutgoingMessageBuilder.WithSticker`; `IStickerService` lists and installs packs (also from `signal.art` share links) |
| 0.3.7 | **Link previews** | D/I | S | ✅ `OutgoingMessageBuilder.WithLinkPreview(url, title, …)`; `Build()` requires the URL in the text |
| 0.3.8 | **Group extras** | A/I | S | ✅ `IGroupService`: `JoinAsync` (accepts an invitation; the API has no join-by-link), `BlockAsync`, `UpdateSettingsAsync` (permissions, invite link mode, timer), `PinMessageAsync`/`UnpinMessageAsync`. Avatars were already covered by `UpdateAsync`. |
| 0.3.9 | **Contact sync** | A/I | S | ✅ `IContactService.SyncAsync`. Contact blocking isn't offered by signal-cli-rest-api, so it's dropped here. |
| 0.3.10 | **Number search** | A/I | S | ✅ `IContactService.CheckRegisteredAsync` → `NumberRegistration` |
| 0.3.11 | **Rate-limit challenge** | A/I | S | ✅ `IAccountService.SubmitRateLimitChallengeAsync(challengeToken, captcha)` |
| 0.3.12 | **Streaming attachments** | A/I | S | ✅ `IAttachmentService.OpenReadAsync` streams downloads (added without breaking existing implementations). Uploads can't stream: `/v2/send` needs base64 in JSON. |
| 0.3.13 | **Typed capabilities** | A | S | ✅ `SignalCapability` plus `SignalApiInfo.Supports` / `EnsureSupported` (descriptive `NotSupportedException`). Upstream currently reports only `v2/send` → `quotes`, `mentions`, so there are two predefined values. |

## Milestone 0.4: Richer incoming model

*Goal: stop dropping envelope content. Each item extends the `EnvelopeContent` union. Because switches over
it are exhaustive, the compiler points at every place that needs updating.*

| # | Feature | Layer | Size | Notes |
|---|---|---|---|---|
| 0.4.1 | **Sync messages** | D/I | M | Messages sent from the account's own devices (`SentTranscript`); enables "note to self" bots and multi-device awareness |
| 0.4.2 | **Edited messages** | D/I | S | `MessageEdited` event with the original timestamp |
| 0.4.3 | **Remote deletes** | D/I | S | `MessageDeleted` event |
| 0.4.4 | **Stickers received** | D/I | S | Sticker metadata on `DataMessage` |
| 0.4.5 | **Story messages** | D/I | M | Opt-in via `Receive:IgnoreStories = false`; `StoryReceived` event |
| 0.4.6 | **Call messages** | D/I | S | `CallReceived` event (offer, hangup) for "sorry, I'm a bot" replies |
| 0.4.7 | **Mention-aware binding** | A | S | `Recipient` and `PhoneNumber` parameters accept `@mentions` (resolving the U+FFFC placeholder through `DataMessage.Mentions`) |
| 0.4.8 | **Text styles received** | D/I | S | Bold, italic and spoiler ranges on `DataMessage` |
| 0.4.9 | **Group change details** | D/I | M | `GroupUpdated` carries what changed (members added or removed, rename) |

## Milestone 0.5: Command system 2.0

*Goal: make complex bots pleasant to write.*

| # | Feature | Layer | Size | Notes |
|---|---|---|---|---|
| 0.5.1 | **Command groups and subcommands** | A | M | `[CommandGroup("group")]` on a module lets `/group add`, `/group remove` share the group's preconditions |
| 0.5.2 | **Conversations and prompts** | A | L | `await Context.PromptAsync<int>("How many?", timeout)` waits for the sender's next message in the conversation. Needs a pending-reply registry checked by the pipeline before command parsing. |
| 0.5.3 | **Background work from commands** | A/H | M | `IBackgroundWorkQueue` so commands can reply later without blocking their partition. Fixes limitation 1. |
| 0.5.4 | **Scheduled messages** | A/H | M | `IMessageScheduler` (in-memory plus a persistence port) for reminders and digests |
| 0.5.5 | **Localisation** | A | M | Resource-based texts for framework replies (unknown command, binding errors, help), with culture per conversation |
| 0.5.6 | **Role-based permissions** | A | M | `[RequireRole("moderator")]` backed by an `IRoleProvider` (config, database or group admins) |
| 0.5.7 | **Per-conversation settings** | A | M | Prefix, language and enabled commands per group, via an `IConversationSettingsStore` port |
| 0.5.8 | **Better help** | A | S | Grouped by module, paging for large bots, examples via `[Example("…")]` |
| 0.5.9 | **"Did you mean …?"** | A | S | Suggest the closest command name on `CommandNotFound`, using edit distance |
| 0.5.10 | **Variadic and collection parameters** | A | S | `params int[] numbers` and `List<PhoneNumber>` binding |
| 0.5.11 | **Reaction commands** | A | S | Trigger handlers by reacting with an emoji (`[OnReaction("👍")]`), e.g. for polls and approvals |

## Milestone 0.6: Scale, persistence and operations

*Goal: run reliably in production and across several instances.*

| # | Feature | Layer | Size | Notes |
|---|---|---|---|---|
| 0.6.1 | **OpenTelemetry** | A/I/H | M | `ActivitySource` spans per envelope and command, and meters for receive lag, queue depth, command duration and API errors. Fits `IHttpClientFactory` and hosting conventions. |
| 0.6.2 | **Receiver health** | H | S | Health check that reports stale WebSocket connections or failing polls, not just `/v1/health` |
| 0.6.3 | **Persistent state ports** | A | M | `ICooldownTracker` / `ISenderRateLimiter` backed by `IDistributedCache` (Redis). Fixes limitation 4. |
| 0.6.4 | **Outbox for sends** | A/I | L | Optional durable queue with retries, so sends survive restarts and API outages without the risk of duplicates |
| 0.6.5 | **Dynamic accounts** | H | M | Add and remove accounts at runtime (`IAccountManager`) instead of only through `Signal:Accounts` |
| 0.6.6 | **Horizontal scale-out** | H | L | Leader election or account leasing, so only one instance receives per account while all can send |
| 0.6.7 | **Graceful drain on shutdown** | H | S | Configurable drain timeout that finishes in-flight commands instead of cancelling them |
| 0.6.8 | **Dead-letter handling** | A | S | `IFailedEnvelopeSink` for envelopes whose processing threw, for later inspection |
| 0.6.9 | **API authentication support** | I | S | Bearer or basic auth headers and mTLS options, for containers behind an authenticating reverse proxy |

## Milestone 0.7: Performance, trimming and Native AOT

*Goal: small, fast-starting bots (containers, Raspberry Pi). Fixes limitation 2.*

| # | Feature | Layer | Size | Notes |
|---|---|---|---|---|
| 0.7.1 | **Command source generator** | T/A | L | Generate `CommandDescriptor`s, parameter binding and module factories at compile time, replacing reflection and `Expression.Compile` |
| 0.7.2 | **Generated event dispatch** | T/A | M | Generate the `IEventHandler<T>` dispatch table instead of `MakeGenericMethod` |
| 0.7.3 | **Generated converters** | T/A | S | Generate `IParsable<T>` converters for the parameter types that are actually used |
| 0.7.4 | **Trimming and AOT annotations** | all | M | `IsAotCompatible=true`, no trim warnings, and an AOT-published sample in CI |
| 0.7.5 | **Roslyn analyzers** | T | M | Build-time diagnostics for invalid command signatures (misplaced `[Remainder]`, unsupported return types, duplicate names), which currently fail only at startup |
| 0.7.6 | **Benchmarks** | T | S | BenchmarkDotNet suite for parsing, binding and pipeline throughput, tracked in CI |

## Milestone 1.0: Stable release

*Goal: a supported, semantically versioned API once .NET 11 and C# 15 are generally available.*

| # | Item | Notes |
|---|---|---|
| 1.0.1 | **Target the .NET 11 GA SDK** | Update `global.json` and drop `LangVersion=preview` once C# 15 ships. *Partly done:* the SDK is pinned to RC1 (from preview 6), and the code builds cleanly on it. |
| 1.0.2 | **API review and freeze** | Review the public surface with the tracked API files; remove experimental members or mark them `[Experimental]` |
| 1.0.3 | **Compatibility policy** | Document semantic versioning, the deprecation process and supported signal-cli-rest-api versions |
| 1.0.4 | **End-to-end test suite** | Testcontainers-based tests against the real signal-cli-rest-api image, covering container-level behaviour without a Signal account (about, health, mode detection, error handling) |
| 1.0.5 | **Migration guide** | Changes since the previews, including the union-based API changes |

---

## Beyond 1.0: Ideas to explore

These need design work or validation of demand first.

| Idea | Description |
|---|---|
| **Direct signal-cli JSON-RPC transport** | An `IMessageReceiver`/`IMessageSender` pair that talks to `signal-cli daemon` over TCP or a Unix socket, without the REST container. The ports make this a drop-in addition. |
| **`Signal.NET.Testing` package** | Publish the test harness and `FakeSignal` so users can unit-test their commands in a few lines |
| **ASP.NET Core integration** | Minimal-API endpoints for sending, status and QR-code linking, plus an optional admin dashboard |
| **AI assistant integration** | An `IMessageMiddleware` that routes non-command messages to an LLM, with conversation history per chat |
| **Moderation toolkit** | Spam detection, link filtering, join approval and kick/ban commands as an optional package |
| **Polls and interactive messages** | Structured polls on top of reactions (and native polls, if and when signal-cli-rest-api exposes them) |
| **Multi-bot hosting** | Several independent bot configurations (commands, options) in one process, keyed by account |
| **Plugin loading** | Load command modules from separate assemblies at runtime, with isolation |
| **Webhook receiver** | Receive envelopes via HTTP callbacks, if signal-cli-rest-api adds support for push delivery |
| **Other hosts** | Samples for Azure Container Apps, Kubernetes (Helm chart for bot plus container), and systemd |

---

## Non-goals

- **Reimplementing the Signal protocol.** Signal.NET builds on signal-cli and signal-cli-rest-api and doesn't talk to Signal servers directly.
- **Mass messaging or spam tooling.** Features that mainly enable unsolicited bulk messaging won't be accepted.
- **Supporting old .NET versions.** The project follows the current .NET release, because it relies on new language features.

## Proposing changes to the roadmap

Open an issue with the *Feature request* template and reference the roadmap item number (e.g. `0.5.2`),
or propose a new item. Items with clear demand and a willing contributor move up.
