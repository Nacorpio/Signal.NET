# Application layer (`Signal.Application`)

**Purpose:** decide *what* the framework does with Signal messages, independently of *how* it talks
to Signal. It defines:

- the ports it needs;
- the options model;
- the per-message pipeline;
- event dispatching;
- the [command system](commands.md).

It has no knowledge of HTTP, JSON or WebSockets.

| Folder | Components |
|---|---|
| `Abstractions/` | Ports (`IMessageSender`, `IMessageReceiver`, …), `ISignalClient`, `SignalApiException`, `SendResult`, `SignalApiInfo`, `ProfileUpdate` |
| `Configuration/` | `SignalOptions` and its sub-options, `SignalOptionsValidator` |
| `Background/` | `IBackgroundWorkQueue`, `BackgroundWorkItem`, `BackgroundWork` (the work's scope, conversation, sender, `ReplyAsync` and `PromptAsync`), `IBackgroundWorkProcessor`, `QueueBackgroundWorkAsync`, `IPromptRegistry`, `PromptResult<T>`, `PromptStatus` |
| `Pipeline/` | `MessageContext`, `IMessageMiddleware`, `MessageDelegate`, `IMessagePipeline`, `MiddlewareRegistry`, built-in middleware, `ISenderRateLimiter` |
| `Events/` | `IEventHandler<TEvent>`, `IDomainEventDispatcher` |
| `Commands/` | See [Command system](commands.md) |
| `/` | `ApplicationServiceCollectionExtensions.AddSignalApplication()` |

---

## Ports (`Abstractions/Ports.cs`)

Ports are interfaces the application layer depends on. The infrastructure layer provides REST
implementations. You can replace any port, for example with a fake in tests or a different transport.

| Port | Purpose | REST endpoint(s) |
|---|---|---|
| `IMessageSender` | Send text, attachments, mentions, quotes and edits; delete a sent message for everyone (`RemoteDeleteAsync`, by its `SendResult.Timestamp`) | `POST /v2/send`, `DELETE /v1/remote-delete/{number}` |
| `IMessageReceiver` | Stream incoming envelopes for one account | `GET /v1/receive/{number}` (poll or WebSocket) |
| `IMessageReceiverFactory` | Pick the receiver for an `ExecutionMode` | – |
| `IReactionService` | Add or remove emoji reactions | `POST`/`DELETE /v1/reactions/{number}` |
| `IReceiptService` | Send read or viewed receipts | `POST /v1/receipts/{number}` |
| `ITypingIndicatorService` | Show or hide "typing…" | `PUT`/`DELETE /v1/typing-indicator/{number}` |
| `IGroupService` | List, get, create, update, members, admins, quit, delete; accept an invitation (`JoinAsync`), block, permissions/invite link/timer (`UpdateSettingsAsync`), pin and unpin messages | `/v1/groups/{number}[/{groupid}[/members\|/admins\|/quit\|/join\|/block\|/pin-message]]` |
| `IAccountService` | List accounts; QR code or raw `sgnl://linkdevice` URI (`GetLinkUriAsync`) for linking the container to an account; set or delete the username; privacy settings; registration lock PIN; lift rate limits with a captcha | `GET /v1/accounts`, `GET /v1/qrcodelink[/raw]`, `/v1/accounts/{number}/username`, `…/settings`, `…/pin`, `…/rate-limit-challenge` |
| `IDeviceService` | The reverse direction: list, link and remove the devices of an account registered in the container. Like `IRegistrationService`, not part of `ISignalClient`. | `GET`/`POST /v1/devices/{number}`, `DELETE /v1/devices/{number}/{deviceId}` |
| `IRegistrationService` | Register a number as the container's primary device (SMS or voice, optional captcha), verify it with the code (and registration lock PIN), unregister it. Not part of `ISignalClient`, because it's account setup, not bot runtime. | `POST /v1/register/{number}[/verify/{code}]`, `POST /v1/unregister/{number}` |
| `IStickerService` | List and install sticker packs, by id and key or from a `https://signal.art/addstickers/#pack_id=…&pack_key=…` share link | `GET`/`POST /v1/sticker-packs/{number}` |
| `IContactService` | List and update contacts; sync them to linked devices (`SyncAsync`); check which numbers are registered with Signal (`CheckRegisteredAsync`) | `GET`/`PUT /v1/contacts/{number}`, `POST …/sync`, `GET /v1/search/{number}?numbers=…` |
| `IAttachmentService` | List, download (`DownloadAsync` into memory, or `OpenReadAsync` as a stream) and delete stored attachments | `/v1/attachments[/{id}]` |
| `IProfileService` | Update name, about and avatar | `PUT /v1/profiles/{number}` |
| `IIdentityService` | List identities; trust keys / safety numbers | `/v1/identities/{number}[/trust/{n}]` |
| `ISystemService` | Version, mode and capabilities; health | `GET /v1/about`, `GET /v1/health` |

Supporting types:

- **`SendResult(Timestamp)`**: the timestamp of a sent message. Keep it to edit the message, react to it or quote it later.
- **`SignalApiInfo`**: the result of `/v1/about`. `Mode` parses the container mode into an `ExecutionMode`. `Supports(SignalCapability)` and `EnsureSupported(SignalCapability)` check optional endpoint features. The latter throws a `NotSupportedException` that names the feature, endpoint and API version, for example `info.EnsureSupported(SignalCapability.SendMentions)` before sending mentions. signal-cli-rest-api currently reports only `SendQuotes` and `SendMentions`.
- **`UsernameAssignment(Username, Link)`**: the result of `IAccountService.SetUsernameAsync`. Signal appends a discriminator (`alice` → `alice.42`); `Link` is a shareable chat link. It's `null` when the API answers without details (204).
- **`AccountSettings(DiscoverableByNumber, ShareNumber)`**: privacy settings for `UpdateSettingsAsync`. `null` leaves a setting unchanged.
- **`RegistrationOptions(UseVoice, Captcha)`**: how `IRegistrationService.RegisterAsync` requests the code. When Signal answers with a captcha error, solve the captcha at `https://signalcaptchas.org/registration/generate.html` and retry with the `signalcaptcha://…` link as `Captcha`.
- **`AttachmentDownload(Content, ContentType, Length)`**: the result of `IAttachmentService.OpenReadAsync`. `Content` reads directly from the HTTP response, so large attachments never have to fit in memory. Dispose it (`await using`) to release the connection. `OpenReadAsync` has a default interface implementation that buffers through `DownloadAsync`, so custom `IAttachmentService` implementations written before it existed keep working.
- **`GroupSettings(Permissions, Link, MessageExpiration)`**: settings for `IGroupService.UpdateSettingsAsync`; `null` leaves a setting unchanged. `GroupPermissions(AddMembers, EditGroup, SendMessages)` sets all three together, because the API requires that. `SendMessages = OnlyAdmins` makes an announcement group. `GroupLinkMode` is `Disabled`, `Enabled` or `EnabledWithApproval`.
- **`NumberRegistration(Number, IsRegistered)`**: an entry of `IContactService.CheckRegisteredAsync`.
- **`StickerPack(PackId, Title, Author, Installed, Url)`**: an entry of `IStickerService.ListAsync`. Only stickers of installed packs can be sent.
- **`LinkedDevice(Id, Name, Created, LastSeen)`**: an entry of `IDeviceService.ListAsync`. `IsPrimary` marks the primary device (id `1`), which `RemoveAsync` refuses to unlink. Timestamps are `null` when signal-cli doesn't know them.
- **`ProfileUpdate(Name, About, Base64Avatar)`**: the values passed to `IProfileService.UpdateAsync`.

### `ISignalClient`

**Purpose:** a convenience facade that exposes every port as a property (`Messages`, `Groups`,
`Reactions`, …). Inject it when a component needs several ports. Inject a single port when it needs
only one, which keeps its dependencies explicit and easy to fake.

### `SignalApiException`

**Purpose:** the API was reached but refused the request. Its properties are `StatusCode` and
`ApiError` (the API's `error` text, for example `Unregistered user`). Connection failures surface as
`HttpRequestException` instead, and timeouts as `TimeoutException` or `OperationCanceledException`.

---

## Configuration (`Configuration/`)

- **`SignalOptions`** is the options tree, bound from the `Signal` section. Every key is listed in the [Configuration reference](configuration.md).
- **`SignalOptionsValidator`** is an `IValidateOptions<SignalOptions>` that collects **all** problems at once. Combined with `ValidateOnStart()`, a misconfigured bot fails immediately at startup with a readable list, instead of failing on the first message.

Notable cross-field rule: in polling modes, `Http:Timeout` must exceed `Receive:TimeoutSeconds` by at
least 5 seconds. Otherwise every long poll would be cut off by the HTTP timeout.

---

## Message pipeline (`Pipeline/`)

**Purpose:** a composable, ASP.NET Core-style chain that every received envelope passes through.

### `MessageContext`

The per-envelope state object:

| Member | Purpose |
|---|---|
| `Envelope`, `Account`, `Sender`, `Conversation` | What was received, and where replies go |
| `Services` | The **scoped** service provider. One scope is created per envelope. |
| `CancellationToken` | Cancelled on host shutdown |
| `Items` | A bag for sharing data between middlewares. The command result is stored under `typeof(CommandResult)`. |
| `IsHandled` | Set by the command middleware. Your middleware can set it too, to suppress command handling. |
| `ReplyAsync(text, quote)` / `ReplyAsync(OutgoingMessage)` | Reply from the receiving account |

### `IMessageMiddleware`, `MessageDelegate`, `IMessagePipeline`

- A middleware implements `InvokeAsync(context, next)`. Calling `next(context)` continues the chain; not calling it stops processing.
- `MessagePipeline` composes the delegate chain **once** (it is a singleton). Each step resolves its middleware from the message scope, so middleware can take scoped dependencies.

### `MiddlewareRegistry`

Keeps the middleware order in three segments:

1. **Leading (built-in):** `ExceptionHandlingMiddleware` → `LoggingMiddleware` → `AccessControlMiddleware` → `RateLimitingMiddleware`
2. **User:** your `AddMiddleware<T>()` registrations, in order
3. **Trailing (built-in):** `DomainEventMiddleware` → `CommandMiddleware`

Because of this order, your middleware sees only messages that passed access control and rate
limiting. It also runs before event handlers and commands, so it can enrich `Items` or short-circuit.

### Built-in middleware

| Middleware | Purpose |
|---|---|
| `ExceptionHandlingMiddleware` | Catches and logs any exception, so one bad message never stops the receive loop. Rethrows only shutdown cancellation. |
| `LoggingMiddleware` | Debug log per envelope: sender, conversation, handled flag, duration |
| `AccessControlMiddleware` | Drops own messages (`IgnoreOwnMessages`), blocked senders, and senders missing from a non-empty allow list |
| `RateLimitingMiddleware` | Drops a sender's data messages beyond `RateLimit:PermitsPerWindow`. Receipts and typing indicators are not limited. |
| `DomainEventMiddleware` | Publishes `Envelope.ToDomainEvent()` (if any) to the event handlers |
| `CommandMiddleware` | Parses and executes commands. See [Command system](commands.md). |

### `ISenderRateLimiter`

**Purpose:** decides whether a sender may send another message. The default is an in-memory fixed
window per sender, which prunes stale entries once more than 10,000 senders are tracked. Replace it
with a distributed implementation when you run several bot instances.

---

## Domain events (`Events/`)

**Purpose:** let features react to things that happened without touching the pipeline.

- **`IEventHandler<TEvent>`:** implement one or more of these per class. Handlers are scoped and run sequentially in registration order.
- **`IDomainEventDispatcher`:** resolves every handler for the event's runtime type. The default implementation builds one delegate per event type and caches it, so there is no per-event reflection.

Event handlers run **before** command handling and receive every event, including messages that are
commands. Check `domainEvent.Message.Text` if a handler should ignore commands.

---

## Registration: `AddSignalApplication()`

Registers everything above with `TryAdd*` and adds the built-in `help` module to the command catalog.
It does **not** register ports; those come from `AddSignalInfrastructure()` or from you.
`GetOrAddSingletonInstance<T>()` is the helper that shares registration-time objects such as
`CommandCatalog` and `MiddlewareRegistry` between the layers and the builder.
