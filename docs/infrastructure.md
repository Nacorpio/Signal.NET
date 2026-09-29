# Infrastructure layer (`Signal.Infrastructure`)

**Purpose:** implement the application ports on top of signal-cli-rest-api. This layer owns every
detail of the wire protocol:

- URLs and query parameters;
- JSON naming;
- error bodies;
- retries;
- WebSockets;
- the envelope format.

| Folder | Components |
|---|---|
| `Http/` | `SignalApiClient`, `SignalRestJsonContext`, `SignalEnvelopeJsonContext` |
| `Http/Dtos/` | REST DTOs (`SendMessageRequestDto`, `GroupDto`, …) and envelope DTOs (`ReceivedMessageDto`, `EnvelopeDto`, …) |
| `Mapping/` | `EnvelopeMapper` |
| `Services/` | REST adapters: one per port |
| `Receiving/` | `ChannelMessageReceiver`, `PollingMessageReceiver`, `WebSocketMessageReceiver`, `IWebSocketConnector` |
| `/` | `AddSignalInfrastructure()` |

---

## HTTP

### `SignalApiClient` (internal)

**Purpose:** a thin typed client (from `IHttpClientFactory`) that only serializes, deserializes and
translates errors.

- **`GetAsync`, `GetBytesAsync`, `SendAsync`:** JSON or binary requests to relative paths such as `v2/send`.
- **Error translation:** a non-success response becomes a `SignalApiException` carrying the API's `error` text.
- **`Escape`:** escapes path segments. Phone numbers become `%2B49…`, and a `/` inside a base64 group id is escaped as well.

### Resilience (configured in `AddSignalInfrastructure`)

The client uses the `Microsoft.Extensions.Http.Resilience` **standard pipeline**:

| Strategy | Setting |
|---|---|
| Attempt timeout | `Signal:Http:Timeout` |
| Total timeout | `Timeout × (RetryCount + 1) + 5 s` |
| Retry | Exponential backoff, `Signal:Http:RetryCount` attempts, **GET/PUT/DELETE only** |
| Circuit breaker | Sampling duration `max(2 × Timeout, 30 s)` |

POST is excluded from retries on purpose. If a send times out *after* the server accepted it,
retrying would deliver the message twice.

### JSON contracts

| Context | Naming | Used for |
|---|---|---|
| `SignalRestJsonContext` | `snake_case` | REST requests and responses (`base64_attachments`, `quote_timestamp`, …) |
| `SignalEnvelopeJsonContext` | `camelCase` (web defaults) | Received envelopes (signal-cli's own format) |

Both contexts are **source-generated** (no runtime reflection; friendly to trimming and native AOT).
Both allow numbers to be read from strings, because the API returns some numbers as strings, such as
the send timestamp. Null values are omitted when writing, so the API applies its own defaults.

The DTOs are internal and never leave this layer.

---

## `EnvelopeMapper`: the anti-corruption layer

**Purpose:** translate signal-cli JSON into `IncomingEnvelope`, isolating every quirk of the format.

| Quirk | Handling |
|---|---|
| Sender in `sourceNumber`/`sourceUuid`, or only the legacy `source` | Tries the specific fields first, then parses `source` as a number or UUID |
| Group ids in envelopes are *internal* ids | Converted with `GroupId.FromInternalId` to the REST form `group.…` |
| `groupInfo.type == "UPDATE"` | `DataMessage.IsGroupUpdate = true`, which raises `GroupUpdated` |
| Authors given as number, UUID or legacy field | Prefers the number, then the UUID, then the legacy field |
| Receipt flags `isDelivery` / `isRead` / `isViewed` | Mapped to `ReceiptType` (viewed > read > delivery) |
| Sync, call and story messages | Not modelled. The envelope is dropped (`null`). |
| No identifiable sender | Dropped |

---

## REST adapters (`Services/RestAdapters.cs`)

One small class per port. Each converts domain types to DTOs, calls `SignalApiClient`, and converts
the response back.

| Adapter | Port | Notes |
|---|---|---|
| `RestMessageSender` | `IMessageSender` | Maps `TextMode.Styled` → `text_mode: "styled"`, quotes, mentions, edits, view-once. Returns the timestamp. `RemoteDeleteAsync` sends `{recipient, timestamp}`; as a DELETE it may be retried, which is harmless for deletes. |
| `RestReactionService` | `IReactionService` | Same body for add (POST) and remove (DELETE) |
| `RestReceiptService` | `IReceiptService` | Rejects `Delivery`, because Signal sends delivery receipts automatically |
| `RestTypingIndicatorService` | `ITypingIndicatorService` | PUT to start, DELETE to stop |
| `RestGroupService` | `IGroupService` | `GetAsync` returns `null` for 404 or 400 (unknown group) |
| `RestAccountService` | `IAccountService` | Invalid numbers in the list are skipped |
| `RestContactService` | `IContactService` | The contact id is its UUID, falling back to the number |
| `RestAttachmentService` | `IAttachmentService` | Returns the raw bytes |
| `RestProfileService` | `IProfileService` | |
| `RestIdentityService` | `IIdentityService` | |
| `RestSystemService` | `ISystemService` | `IsHealthyAsync` never throws for connection errors |

All adapters are **transient**, because they wrap the typed `HttpClient`, whose handler lifetime
`IHttpClientFactory` manages.

---

## Receivers (`Receiving/`)

### `ChannelMessageReceiver` (public base class)

**Purpose:** the shared plumbing for any transport.

- **Producer and channel:** a background producer task writes into a **bounded channel** (256 by default). `ReceiveAsync` exposes the channel as `IAsyncEnumerable<IncomingEnvelope>`.
- **Back-pressure:** when the consumer is slow, `WriteAsync` waits, which pauses polling or socket reading.
- **Stopping:** when the consumer stops enumerating, the producer is cancelled and awaited.
- **Backoff:** `Backoff(attempt, min, max)` is exponential with ±20 % jitter, so many bots reconnecting after an outage don't all retry at the same moment.

A custom transport only has to implement `ProduceAsync`.

### `PollingMessageReceiver` (`normal`, `native`)

1. Calls `GET v1/receive/{number}?timeout=…&ignore_attachments=…&ignore_stories=…&send_read_receipts=…[&max_messages=…]`.
2. Maps each item with `EnvelopeMapper` and writes it to the channel.
3. If the batch was empty, waits `PollingInterval`. If it wasn't, polls again immediately to drain a backlog.
4. On errors, logs and waits with exponential backoff up to `MaxErrorBackoff`.

Options are re-read on every iteration, so interval changes apply without a restart.

### `WebSocketMessageReceiver` (`json-rpc`, `json-rpc-native`)

1. Builds the URI: `http`→`ws`, `https`→`wss`, and any reverse-proxy base path is kept (`https://host/api/` → `wss://host/api/v1/receive/%2B49…`).
2. Connects through `IWebSocketConnector`.
3. Reads messages into a pooled buffer, **reassembling fragmented frames**, deserializes each text message and maps it. Invalid frames are logged and skipped.
4. On a server close or an error, reconnects with backoff between `ReconnectMinDelay` and `ReconnectMaxDelay`. The backoff resets after every successful connection.

### `IWebSocketConnector`

**Purpose:** a seam for opening sockets. The default, `ClientWebSocketConnector`, applies the keep-alive
interval. Replace it to add authentication headers, a proxy or custom TLS settings, or to connect to an
in-memory or loopback server in tests (see `ReceiverTests`).

---

## Registration: `AddSignalInfrastructure()`

- Typed `HttpClient` for `SignalApiClient`, with the base address and the resilience pipeline
- All adapters (transient, `TryAdd`)
- `IWebSocketConnector` (singleton)
- Receivers as **keyed services** by `ExecutionMode`, plus `IMessageReceiverFactory`
- A non-keyed `IMessageReceiver` that resolves the receiver for the configured mode
