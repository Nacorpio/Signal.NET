# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.5.0-preview.1] - 2026-09-30

Milestone 0.5 (command system 2.0). Every change is binary-compatible with 0.4.0-preview.1: new members on existing
interfaces are default interface members, and new registration helpers are extension methods.

### Added

- **Command groups (roadmap 0.5.1):** `[CommandGroup("playlist", Aliases = ["pl"])]` on a module or `ICommand` class makes its commands `/playlist add`, `/pl add`, and so on. Module preconditions apply to the whole group.
  - `CommandDescriptor.Group` (`CommandGroupInfo`) and `FullName`; `ICommandRegistry.GetGroup` (default interface member).
  - `/group` alone, or with an unknown subcommand, replies with the group's commands. `/help group` describes a group.
- **Prompts (roadmap 0.5.2):** `BackgroundWork.PromptAsync(question)` and `PromptAsync<T>` (any `IParsable<T>`, re-asking on invalid input) wait for the triggering sender's next message in the conversation, and return a `PromptResult<T>` (`Answered`, `TimedOut`, `Invalid`).
  - The host routes answers through the new `IPromptRegistry` before partitioning. Commands (by the conversation's own prefixes) are never consumed as answers.
  - New option `Signal:Background:PromptTimeout` (2 minutes). `BackgroundWork.Sender` and `BackgroundWorkItem.Sender` identify who may answer.
- **Background work (roadmap 0.5.3):** `CommandModule.RunInBackgroundAsync(work => …)` (or `MessageContext.QueueBackgroundWorkAsync`, or `IBackgroundWorkQueue`) runs slow work outside the conversation partition, so later messages aren't held up.
  - Each item runs in its own DI scope and can reply to the original conversation with `work.ReplyAsync`.
  - The queue is bounded and in memory, configured by `Signal:Background:MaxConcurrency` (4) and `Capacity` (100). `AddSignal` hosts the processor (`IBackgroundWorkProcessor`).
  - At shutdown, running work is cancelled and queued items are dropped with a warning.
- **Scheduled messages (roadmap 0.5.4):** `CommandModule.ScheduleReplyAsync` / `MessageContext.ScheduleReplyAsync` and `IMessageScheduler` (`ScheduleAsync`, `ListAsync`, `CancelAsync`) send text messages later, once or every `repeatEvery` (at least one minute).
  - `IScheduledMessageStore` is the persistence port; the default keeps messages in memory.
  - `AddSignal` hosts the dispatcher, which checks every `Signal:Scheduler:PollInterval` (1 s) and retries failed sends after `RetryDelay` (1 min).
  - After downtime, a recurring message sends one catch-up, not one per missed occurrence.
- **Localisation (roadmap 0.5.5):** every text the framework sends (errors, usage, help, precondition failures, prompt re-asks) goes through the new `ISignalTexts`, with keys in `TextKey`.
  - Translations go under `Signal:Localization:Texts:{culture}:{key}`. Lookup falls back `de-AT` → `de` → English.
  - The culture is the conversation's `ConversationSettings.Culture`, otherwise `Localization:DefaultCulture`. Cultures are plain names, so this works in invariant-globalization mode too.
  - Argument type names are translated via `Type.{English name}`. Custom preconditions can use `Fail(context, key, args)`, and `MessageContext.GetCultureAsync()` exposes the culture.
  - English output is unchanged.
- **Roles (roadmap 0.5.6):** `[RequireRole("moderator", Role.GroupAdmin)]` passes when the sender has any listed role.
  - Roles come from `IRoleProvider`s: `Signal:Commands:Roles` (role → numbers or UUIDs), `admin` (includes `Commands:Admins`) and `group-admin` (Signal group admins, looked up live).
  - `AddRoleProvider<T>()` adds your own provider, e.g. database-backed. `IRoleService` checks roles outside commands.
- **Per-conversation settings (roadmap 0.5.7):** `ConversationSettings` per group or direct chat: `Prefixes` (replace the global ones there), `DisabledCommands` (full or group names) and `Culture`.
  - Stored through the new `IConversationSettingsStore` port (in memory by default) and read once per message via `MessageContext.GetConversationSettingsAsync()`.
  - Disabled commands reply with `Commands:DisabledCommandMessage`, and help and suggestions hide them.
  - `ICommandParser.TryParse(text, prefixes, …)` is a new default interface member.
- **Better help (roadmap 0.5.8):** `/help` lists commands under headings for each command group and `[Category("…")]` (a flat list when there's only one section). It pages by `Commands:HelpPageSize` (default 20; `/help 2`). `/help <command>` shows `[Example("…")]`s. `CommandDescriptor.Category` and `Examples` describe them.
- **"Did you mean …?" (roadmap 0.5.9):** unknown-command replies suggest the closest visible command, alias, group or subcommand (`/hlep` → "Did you mean /help?"). Adjacent swaps count as one typo. Controlled by `Commands:SuggestSimilarCommands` (default `true`).
- **Collection parameters (roadmap 0.5.10):** `params int[] numbers`, `List<PhoneNumber> people`, `IReadOnlyList<string> tags`, and other array, list or collection-interface parameters take all remaining positional arguments, each converted (and `@mention`-resolved) individually. `CommandParameter.ElementType` and `IsCollection` describe them.
- **Reaction commands (roadmap 0.5.11):** `ReactionModule` methods with `[OnReaction("👍")]` run when someone reacts to the bot's messages (or any message with `AnyMessage`, removals with `IncludeRemovals`).
  - Emojis match regardless of skin tone and variation selector.
  - Registered with `AddReactionModule<T>()` or `AddCommands(assembly)`; invalid handler signatures fail at registration.

### Changed

- Cooldowns, usage lines, logs and `CommandDescriptor.ToString()` use the full command name (`playlist add`). Nothing changes for commands outside a group.
- `/help` takes the rest of the text, so `/help playlist add` works.

## [0.4.0-preview.1] - 2026-09-30

Milestone 0.4 (richer incoming model). Every change is binary-compatible with 0.3.0-preview.1. New
`EnvelopeContent` cases raise compiler warnings in exhaustive switches, and a few behaviors changed (see below).

### Added

- **Sync messages (roadmap 0.4.1):** `EnvelopeContent` has a new case, `SentTranscript`, for messages the account sent from its other devices, raising the new `MessageSent` event. `IncomingEnvelope.Conversation` is the transcript's destination. Transcripts never run commands. They are delivered only with `AccessControl:IgnoreOwnMessages = false`, so the default behavior is unchanged.
- **Edits, remote deletes and received stickers (roadmap 0.4.2, 0.4.3, 0.4.4):**
  - `EnvelopeContent` has a new case, `EditMessage` (edited message's timestamp plus the new version), raising the new `MessageEdited` event. Edits never run commands.
  - `DataMessage.RemoteDelete` raises the new `MessageDeleted` event.
  - `DataMessage.Sticker` carries received stickers.
- **Stories (roadmap 0.4.5):** `EnvelopeContent` has a new case, `StoryMessage` (file or text, optional group, `AllowsReplies`), raising the new `StoryReceived` event. Opt-in via `Receive:IgnoreStories = false`.
- **Calls (roadmap 0.4.6):** `EnvelopeContent` has a new case, `CallMessage` (`CallEventKind` offer, answer, busy or hangup; `CallId`; `IsVideo` for offers), raising the new `CallReceived` event.
- **Mention-aware binding (roadmap 0.4.7):** a positional command argument that is an `@mention` binds as the mentioned user's phone number (or UUID if hidden), so `/kick @Bob` works with `Recipient`, `PhoneNumber` and `AccountId` parameters. A placeholder without a matching mention fails with `Could not resolve the @mention for <name>.` Before, it would have been accepted as a username.
- **Received text styles (roadmap 0.4.8):** `DataMessage.TextStyles` lists the sender's formatting as `StyledRange(TextStyle, Start, Length)`.
- **Group update context (roadmap 0.4.9):** `GroupUpdated` has `Name` and `Revision`, and every group message exposes `DataMessage.GroupName` and `GroupRevision`. Signal doesn't report what changed; compare revisions or fetch the group to find out.

### Changed

- `Receive:IgnoreStories` is now enforced in the WebSocket (`json-rpc*`) modes too. Before, it was only passed to the API when polling.
- Exhaustive `switch`es over `EnvelopeContent` now produce a warning until they handle `EditMessage`, `SentTranscript`, `StoryMessage` and `CallMessage`. This is source-compatible and binary-compatible.
- Sticker-only messages now raise `MessageReceived` (they were dropped before). `DataMessage.HasContent` is `true` for them, while `Text` is `null`.

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

[Unreleased]: https://github.com/Nacorpio/Signal.NET/compare/v0.5.0-preview.1...HEAD
[0.5.0-preview.1]: https://github.com/Nacorpio/Signal.NET/compare/v0.4.0-preview.1...v0.5.0-preview.1
[0.4.0-preview.1]: https://github.com/Nacorpio/Signal.NET/compare/v0.3.0-preview.1...v0.4.0-preview.1
[0.3.0-preview.1]: https://github.com/Nacorpio/Signal.NET/compare/v0.2.0-preview.2...v0.3.0-preview.1
[0.2.0-preview.2]: https://github.com/Nacorpio/Signal.NET/compare/v0.2.0-preview.1...v0.2.0-preview.2
[0.2.0-preview.1]: https://github.com/Nacorpio/Signal.NET/releases/tag/v0.2.0-preview.1
