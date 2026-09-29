# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.3.0-preview.1] - 2026-09-29

Milestone 0.3 (complete API coverage). Every change is additive: members added to existing interfaces have default
implementations, so code written against 0.2.0-preview.2 keeps compiling.

### Added

- **Typed capabilities (roadmap 0.3.13):** `SignalCapability` (with `SendQuotes` and `SendMentions`), `SignalApiInfo.Supports` and `SignalApiInfo.EnsureSupported`. The latter throws a `NotSupportedException` naming the missing feature and the API version.
- **Group extras, contact sync and number search (roadmap 0.3.8, 0.3.9, 0.3.10):**
  - New `IGroupService` members: `JoinAsync` (accept an invitation), `BlockAsync`, `UpdateSettingsAsync` (`GroupPermissions`, `GroupLinkMode`, disappearing-messages timer), `PinMessageAsync` and `UnpinMessageAsync`.
  - New `IContactService` members: `SyncAsync` sends contacts to linked devices; `CheckRegisteredAsync` reports which numbers are registered with Signal.
  - All are default interface members, so existing implementations keep compiling.
- **Stickers and link previews (roadmap 0.3.6, 0.3.7):**
  - `OutgoingMessageBuilder.WithSticker` sends a sticker. The new `Sticker` value object parses `packId:stickerId` and binds as a command argument.
  - The new `IStickerService` lists and installs sticker packs, by id and key or from a `signal.art` share link.
  - `OutgoingMessageBuilder.WithLinkPreview` attaches a preview card. `Build()` rejects previews whose URL isn't in the text, and stickers combined with attachments.
- **Device management (roadmap 0.3.2):**
  - The new `IDeviceService` lists (`LinkedDevice`), links and removes the devices of an account registered in the container.
  - `IAccountService.GetLinkUriAsync` returns the raw `sgnl://linkdevice` URI, so you can render your own QR code. It's a default interface member.
- **Account management (roadmap 0.3.3, 0.3.4, 0.3.11):** new `IAccountService` members:
  - `SetUsernameAsync` and `DeleteUsernameAsync`;
  - `UpdateSettingsAsync` (discoverability, number sharing);
  - `SetPinAsync` and `RemovePinAsync` for the registration lock;
  - `SubmitRateLimitChallengeAsync` to lift a rate limit with a captcha.

  They are default interface members, so existing implementations keep compiling.

- **Registration and verification (roadmap 0.3.1):**
  - The new `IRegistrationService` registers a number as the container's primary device (`RegisterAsync` by SMS or voice, with an optional captcha).
  - `VerifyAsync` completes the registration with the received code and an optional registration-lock PIN.
  - `UnregisterAsync` removes the number again.
  - Calls are never retried, so no duplicate verification codes are sent.
- **Remote delete (roadmap 0.3.5):** `IMessageSender.RemoteDeleteAsync` deletes a sent message for everyone, identified by its `SendResult.Timestamp`. It's a default interface member, so existing `IMessageSender` implementations keep compiling.
- **Streaming attachment downloads (roadmap 0.3.12):** `IAttachmentService.OpenReadAsync` returns an `AttachmentDownload` whose stream reads directly from the HTTP response, with content type and length. It's a default interface member, so existing `IAttachmentService` implementations keep compiling.

- **Release workflow:** waits until every package is listed on nuget.org before creating the GitHub Release (roadmap 0.2.8).
- **Package validation:** compares against the published `0.2.0-preview.2` baseline, so breaking API changes fail `dotnet pack`.

## [0.2.0-preview.2] - 2026-09-29

First release on nuget.org, under the new `Nacorpio.Signal.*` package IDs.

### Changed

- **NuGet package IDs are now owner-prefixed:**
  - `Nacorpio.Signal.Domain`, `Nacorpio.Signal.Application`, `Nacorpio.Signal.Infrastructure` and `Nacorpio.Signal.Hosting`.
  - The template package is now `Nacorpio.Signal.Templates`.
  - The `Signal.` ID prefix is reserved by another owner on nuget.org, so the 0.2.0-preview.1 packages were never published.
  - Assembly names and namespaces are unchanged.
- **Release workflow:** a rejected nuget.org push now fails the release instead of being skipped as a duplicate.

## [0.2.0-preview.1] - 2026-09-29

First preview release. Available as a [GitHub Release](https://github.com/Nacorpio/Signal.NET/releases/tag/v0.2.0-preview.1) only: publishing to nuget.org failed because the `Signal.` package ID prefix is reserved.

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

[Unreleased]: https://github.com/Nacorpio/Signal.NET/compare/v0.3.0-preview.1...HEAD
[0.3.0-preview.1]: https://github.com/Nacorpio/Signal.NET/compare/v0.2.0-preview.2...v0.3.0-preview.1
[0.2.0-preview.2]: https://github.com/Nacorpio/Signal.NET/compare/v0.2.0-preview.1...v0.2.0-preview.2
[0.2.0-preview.1]: https://github.com/Nacorpio/Signal.NET/releases/tag/v0.2.0-preview.1
