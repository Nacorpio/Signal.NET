# Domain layer (`Signal.Domain`)

**Purpose:** model Signal concepts as types that cannot be invalid. Everything else in the framework
speaks this language. The project has no dependencies, performs no I/O, and never references DTOs or HTTP.

| Folder | Components |
|---|---|
| `/` | `ExecutionMode`, `ExecutionModeExtensions` |
| `ValueObjects/` | `PhoneNumber`, `AccountId`, `Username`, `GroupId`, `Recipient` (union) |
| `Messaging/` | `IncomingEnvelope`, `EnvelopeContent` (union), `DataMessage`, `EditMessage`, `RemoteDelete`, `SentTranscript`, `StoryMessage`, `CallMessage`, `CallEventKind`, `ReceiptMessage`, `TypingMessage`, `Sender`, `OutgoingMessage`, `OutgoingMessageBuilder`, `Attachment`, `Mention`, `Quote`, `Reaction`, `TextMode`, `ReceiptType`, `TypingAction` |
| `Entities/` | `Entity<TId>`, `Group`, `Contact`, `Identity` |
| `Events/` | `IDomainEvent`, `DomainEvent`, `MessageReceived`, `MessageEdited`, `MessageDeleted`, `MessageSent`, `StoryReceived`, `CallReceived`, `ReactionReceived`, `ReceiptReceived`, `TypingIndicatorChanged`, `GroupUpdated` |
| `Exceptions/` | `SignalDomainException`, `InvalidPhoneNumberException`, `InvalidRecipientException` |

---

## `ExecutionMode`

**Purpose:** names the container's `MODE` and captures its consequences in one place.

| Value | Container `MODE` | `IsStreaming` | `IsNative` |
|---|---|---|---|
| `Normal` | `normal` | false | false |
| `Native` | `native` | false | true |
| `JsonRpc` | `json-rpc` | true | false |
| `JsonRpcNative` | `json-rpc-native` | true | true |

`ExecutionModeExtensions` uses C# 14 extension members to add the properties `IsStreaming`, `IsNative`
and `ContainerValue` to the enum. `TryParseContainerValue("json-rpc", out mode)` accepts both container
spellings and enum names; `SignalApiInfo.Mode` uses it to interpret `/v1/about`.
See [Execution modes](execution-modes.md).

---

## C# 15 unions in the domain

The domain uses [C# 15 union types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union)
wherever a value is **exactly one of a closed set of types**:

| Union | Case types | Replaces |
|---|---|---|
| `Recipient` | `PhoneNumber`, `AccountId`, `Username`, `GroupId` | A record with a `RecipientKind` enum and a `string` value |
| `EnvelopeContent` | `DataMessage`, `ReceiptMessage`, `TypingMessage` | Three nullable properties with an "at least one is set" convention |

What unions give you:

- **Implicit conversions from every case type:** `Recipient r = phoneNumber;` and `new IncomingEnvelope(…, dataMessage)`.
- **Pattern matching on the contained value:** `recipient is GroupId`.
- **Exhaustiveness checks:** a `switch` that misses a case type produces a compiler warning, which is an error in this repository.

A union is a struct whose `Value` property is `object?`. `default` holds no value, which is why switches
over a union that might be `default` include a `null` arm.

---

## Value objects

Value objects are immutable, compare by value, and validate when they are created. Each exposes
`Parse` (throws a domain exception) and `TryParse` (returns `false`).

### `PhoneNumber`

**Purpose:** a guaranteed-valid E.164 number (`+` followed by 7 to 15 digits, not starting with 0).

- **Normalisation:** spaces, dashes, dots and parentheses are removed, and a leading `00` becomes `+`. `"+49 151-1234 5678"` and `"004915112345678"` both parse to `+4915112345678`.
- **`IParsable<PhoneNumber>`:** because it implements this interface, command parameters of type `PhoneNumber` bind automatically.
- **Errors:** invalid input throws `InvalidPhoneNumberException`.

### `GroupId`

**Purpose:** the group identifier in the form the REST API expects: `group.` followed by the base64 of the internal id.

signal-cli puts the *internal* id inside received envelopes, but the REST endpoints and `/v2/send`
recipients need the `group.…` form. `GroupId` makes that conversion explicit:

- `GroupId.FromInternalId("abc123==")` returns `group.YWJjMTIzPT0=`.
- The `InternalId` property converts back.
- `Parse`/`TryParse` accept only the `group.` form.

It implements `IParsable<GroupId>`.

### `AccountId`

**Purpose:** a Signal account identifier (ACI): a UUID that identifies an account independently of its
phone number. Use it to address users who hide their number. `ToString()` returns the hyphenated
lower-case form the API expects. It implements `IParsable<AccountId>`.

### `Username`

**Purpose:** a Signal username such as `alice.42`. It is trimmed, never empty, and never contains whitespace.
Invalid input throws `InvalidRecipientException`. It implements `IParsable<Username>`.

### `Recipient` (union)

**Purpose:** anything a message can be addressed to.

```csharp
public union Recipient(PhoneNumber, AccountId, Username, GroupId) { … }
```

| Member | Purpose |
|---|---|
| `Value` (generated) | The contained case value, as `object?` |
| `Address` | The string sent to the REST API: the E.164 number, UUID, username or `group.…` id |
| `IsGroup` | `this is GroupId` |
| `Parse` / `TryParse` | Auto-detects the case: group id first, then phone number, then UUID, otherwise username |

Creating and inspecting a recipient:

```csharp
Recipient byNumber = PhoneNumber.Parse("+4915112345678");   // implicit union conversion
Recipient byGroup  = GroupId.FromInternalId(internalId);
OutgoingMessage.To(byNumber, byGroup);

var label = recipient switch                                 // exhaustive: every case must be handled
{
    PhoneNumber n => $"phone {n}",
    AccountId id => $"account {id}",
    Username u => $"@{u}",
    GroupId g => $"group {g}",
    null => "nobody",                                        // default(Recipient)
};
```

A `default(Recipient)` holds no value. `Address` throws for it, and `OutgoingMessageBuilder.To` rejects
it with an `ArgumentException`.

---

## Messaging

### `IncomingEnvelope` (aggregate root)

**Purpose:** the single entry point for everything received. It knows its conversation and which domain events it represents.

| Member | Meaning |
|---|---|
| `Account` | The receiving account |
| `Source` | The `Sender` |
| `Timestamp` / `ReceivedAt` | Server timestamp (ms) / as a `DateTimeOffset` |
| `Content` | The `EnvelopeContent` union: **exactly one** of `DataMessage`, `EditMessage`, `SentTranscript`, `StoryMessage`, `CallMessage`, `ReceiptMessage`, `TypingMessage` |
| `Data` / `Edit` / `Transcript` / `Story` / `Call` / `Receipt` / `Typing` | Convenience accessors. Each returns the content if it has that type, otherwise `null`. |
| `Group`, `IsGroup` | Group context, from the data message, the edited or sent message, a group story, or the typing message |
| `Conversation` | **Where replies go:** the group recipient for group messages, otherwise the sender (by phone number, or by `AccountId` if the number is hidden). For a `SentTranscript` it is the transcript's destination, because the sender is the account itself. |
| `ToDomainEvent()` | Converts the envelope into its domain event (see below) |

`ToDomainEvent()` is an exhaustive switch over `Content`:

| Content | Event |
|---|---|
| `DataMessage` with a `RemoteDelete` | `MessageDeleted` |
| `DataMessage` with a `Reaction` | `ReactionReceived` |
| `DataMessage` with `IsGroupUpdate` and a group | `GroupUpdated` |
| `DataMessage` with text, attachments or a sticker | `MessageReceived` |
| `DataMessage` without content | none (`null`) |
| `EditMessage` | `MessageEdited` |
| `SentTranscript` | `MessageSent` |
| `StoryMessage` | `StoryReceived` |
| `CallMessage` | `CallReceived` |
| `ReceiptMessage` | `ReceiptReceived` |
| `TypingMessage` | `TypingIndicatorChanged` |

### `EnvelopeContent` (union)

```csharp
public union EnvelopeContent(DataMessage, EditMessage, SentTranscript, StoryMessage, CallMessage, ReceiptMessage, TypingMessage);
```

**Purpose:** a Signal envelope carries exactly one kind of content. Modelling that as a union makes it
impossible to build an envelope with no content or with two kinds at once. Adding a new content kind
produces a compiler warning at every `switch` that doesn't handle it yet. `EditMessage`, `SentTranscript`, `StoryMessage` and
`CallMessage` were added this way in 0.4, so exhaustive switches written against 0.3 now warn until they handle them.

### `DataMessage`

**Purpose:** a regular message.

- **Content:** `Timestamp` and `Text`, plus `Group`, `Attachments`, `GroupName`, `GroupRevision`, `Mentions`, `TextStyles`, `Quote`, `Reaction`, `Sticker`, `RemoteDelete`, `IsGroupUpdate` and `ViewOnce`.
- **`TextStyles`:** the sender's formatting as `StyledRange(Style, Start, Length)` entries, with `TextStyle` being `Bold`, `Italic`, `Spoiler`, `Strikethrough` or `Monospace`. Positions are UTF-16 offsets into `Text`, like mentions.
- **`HasContent`:** true when there is text, at least one attachment, or a sticker.
- **`Sticker`:** a received sticker as the same `Sticker` value object used for sending, so a bot can send it back with `WithSticker`.
- **`RemoteDelete`:** set when the message deletes an earlier one for everyone. `TargetTimestamp` together with the envelope's sender identifies the deleted message, because Signal only lets authors delete their own messages.
- **Why the timestamp matters:** it identifies the message for reactions, quotes and receipts.

### `EditMessage`

**Purpose:** an edit of an earlier message. signal-cli reports edits at envelope level rather than inside a
data message, so `EditMessage` is its own union case: `TargetTimestamp` names the edited message and `Message`
is the new version (a full `DataMessage`, with its own timestamp). **Edits never run commands**, so editing a
message into `/something` doesn't execute it; handle `MessageEdited` to react to edits.

### `SentTranscript`

**Purpose:** a copy of a message the receiving account sent from one of its **other devices** (for example
typed on the phone), delivered by Signal as a sync message. `Conversation` is where it went: the group, the
direct-message recipient, or the account itself for "note to self". `Message` is the sent `DataMessage`; for a
transcript of an edit, `EditTargetTimestamp` names the edited message.

- **Transcripts never run commands.** Two bot instances sharing an account therefore can't trigger each other
  in a loop. Handle `MessageSent` instead.
- **They're filtered by default.** Their sender is the account itself, so `AccessControl:IgnoreOwnMessages`
  (default `true`) drops them. Set it to `false` to receive `MessageSent`. If you use `AllowedSenders`, include
  the account's own number too.

### `StoryMessage`

**Purpose:** a story posted by the sender, to their contacts or (with `Group` set) to a group. A media story has
`File` (an `Attachment`), a text story has `Text`; colors and gradients aren't modelled. `AllowsReplies` says
whether it accepts replies. Stories are **opt-in**: set `Receive:IgnoreStories = false`. The option is enforced
in every execution mode (the WebSocket modes can't ask the API to skip them, so the mapper drops them).
Stories never run commands.

### `CallMessage`

**Purpose:** a voice or video call event: `Kind` (`Offer`, `Answer`, `Busy`, `Hangup`) and `CallId`, which links
the events of one call. For offers, `IsVideo` says whether it's a video call. Signal.NET can't take calls; use
`CallReceived` to react, for example by replying to an `Offer` that the bot can't take calls. Low-level ICE
updates are not modelled.

### `ReceiptMessage`, `TypingMessage`

**Purpose:** delivery, read and viewed receipts (`ReceiptType`, `When`, `Timestamps` of the acknowledged messages), and typing indicators (`TypingAction.Started` or `Stopped`, with an optional group).

### `Sender`

**Purpose:** who sent an envelope. `Number` and `Uuid` are both optional, but at least one is always present, because the mapper drops envelopes that have neither.

- **`Identifier`:** the phone number if known, otherwise the UUID.
- **`ToRecipient()`:** addresses the sender directly.
- **`Matches(string)`:** true if a phone number (in any accepted format) or a UUID string identifies this sender. Admin lists, allow lists and block lists use it.

### `OutgoingMessage` / `OutgoingMessageBuilder`

**Purpose:** an immutable, validated message ready to send. Build it fluently:

```csharp
var message = OutgoingMessage.To(recipientA, recipientB)
    .WithStyledText("**Hello** @Bob")        // or WithText(text, TextMode.Normal)
    .WithMention("+4915112345678", 6, 4)     // author, start, length
    .WithAttachment(pngBytes, "image/png", "chart.png")
    .Quoting(originalTimestamp, originalAuthor, "original text")
    .Editing(previousTimestamp)              // edit a message you sent earlier
    .AsViewOnce()
    .NotifySelf()
    .Build();
```

| Builder method | Effect |
|---|---|
| `To(...)` | Adds recipients. Duplicates are ignored. |
| `WithText` / `WithStyledText` | Sets the text. Styled mode enables `**bold**`, `*italic*`, `~strike~`, `` `mono` `` and `\|\|spoiler\|\|`. |
| `WithAttachment(bytes, contentType, fileName?)` | Encodes the content as `data:<mime>;filename=<name>;base64,<data>` |
| `WithBase64Attachment(string)` | Adds content that is already base64 or a data URI |
| `WithMention(author, start, length)` | Mentions a user at the given position in the text |
| `Quoting(timestamp, author, text?)` | Replies to (quotes) a message |
| `Editing(timestamp)` | Turns the send into an edit of a message sent earlier |
| `AsViewOnce()`, `NotifySelf()` | Signal send flags |
| `WithSticker(Sticker)` / `WithSticker(packId, stickerId)` | Sends a sticker from a pack installed on the account (see `IStickerService`). A sticker is valid content on its own. |
| `WithLinkPreview(url, title?, description?, base64Thumbnail?)` | Attaches a preview card for a link in the text. Only absolute `http`/`https` URLs are accepted. |

Outgoing attachments can't be streamed: `POST /v2/send` expects them as base64 inside the JSON body, so the
whole file is encoded in memory. Downloads, in contrast, can stream (`IAttachmentService.OpenReadAsync`).

`Build()` throws `SignalDomainException` if the message:

- has no recipient;
- has no text, attachment or sticker;
- has a mention that extends past the end of the text;
- combines a sticker with attachments;
- has a link preview whose URL isn't in the text. Signal clients would silently drop such a preview, so it fails early instead.

### `Sticker`

**Purpose:** a reference to a sticker, written as `packId:stickerId`. `PackId` is hex and normalized to lower
case. `Sticker` implements `IParsable<Sticker>`, so a command parameter of type `Sticker` binds directly from
an argument like `f3a9…:4`.

### Small value records

| Type | Purpose |
|---|---|
| `Attachment(Id, ContentType, Filename, Size)` | Metadata of a received attachment. Download it through `IAttachmentService` by `Id`. |
| `Mention(Author, Start, Length, Name)` | A mention inside a text. `Author` is a phone number or UUID. |
| `StyledRange(Style, Start, Length)`, `TextStyle` | A formatted range of a received text |
| `Quote(Timestamp, Author, Text)` | A quoted message |
| `Reaction(Emoji, TargetAuthor, TargetTimestamp, IsRemove)` | An emoji reaction, or its removal |
| `LinkPreview(Url, Title, Description, Base64Thumbnail)` | A link preview card of an outgoing message |
| `TextMode` | `Normal` or `Styled` |

---

## Entities

### `Entity<TId>`

**Purpose:** the base class for types that have identity. Two entities are equal when they have the same runtime type and `Id`, regardless of their other state.

### `Group`

**Purpose:** a snapshot of a Signal group, as returned by `IGroupService`.

- **Id and details:** `Id` (a `GroupId`), `Name`, `Description`, `IsBlocked`, `InviteLink`.
- **Membership:** `Members`, `Admins` and `PendingInvites` are case-insensitive sets.
- **Behaviour:** `HasMember(identifier)` and `IsAdmin(identifier)`. The `[RequireGroupAdmin]` precondition uses `IsAdmin`.

### `Contact`

**Purpose:** an entry in the account's contact list: `Number`, `Uuid`, `Name`, `ProfileName`, `Username` and `IsBlocked`. `DisplayName` returns the first of these that is available: name, then profile name, username, number, and finally the id.

### `Identity`

**Purpose:** a safety-number / trust record for a contact: `Status` (for example `TRUSTED_VERIFIED`), `Fingerprint`, `SafetyNumber` and `Added`. `IsTrusted` is true for any `TRUSTED*` status.

---

## Domain events

**Purpose:** decouple reactions to what happened ("a reaction arrived") from the code that
receives messages. Events are raised by `IncomingEnvelope.ToDomainEvent()` and delivered to
`IEventHandler<TEvent>` implementations by the application layer.

| Event | Payload | Typical use |
|---|---|---|
| `MessageReceived` | `Message` (`DataMessage`) | Logging, auto-replies, moderation, non-command bots |
| `MessageEdited` | `Edit` (`EditMessage`) | Keeping stored copies or moderation up to date |
| `MessageDeleted` | `Delete` (`RemoteDelete`) | Removing stored copies (e.g. logs, archives) |
| `StoryReceived` | `Story` (`StoryMessage`) | Story archives, reactions to stories. Requires `IgnoreStories = false`. |
| `CallReceived` | `Call` (`CallMessage`) | "Sorry, I'm a bot" replies to call offers |
| `MessageSent` | `Transcript` (`SentTranscript`) | "Note to self" bots, multi-device awareness. Requires `IgnoreOwnMessages = false`. |
| `ReactionReceived` | `Reaction` | Polls, acknowledgements |
| `ReceiptReceived` | `Receipt` | Delivery tracking |
| `TypingIndicatorChanged` | `Typing` | Presence features |
| `GroupUpdated` | `Group`, `Name`, `Revision` | Welcome messages, audit logs. Signal doesn't say *what* changed: compare `Revision` with a stored value, or fetch the group with `IGroupService.GetAsync`. |

Every event implements `IDomainEvent`, exposing `Envelope` (the full context) and `OccurredAt`
(the envelope's timestamp).

---

## Exceptions

| Type | Thrown when |
|---|---|
| `SignalDomainException` | Base class. Thrown for any violated invariant, such as an invalid outgoing message or a sender without an identifier. |
| `InvalidPhoneNumberException` | `PhoneNumber.Parse` fails. `Value` holds the input. |
| `InvalidRecipientException` | `Recipient`/`GroupId` parsing fails. `Value` holds the input. |
