# Configuration reference

All settings live in the `Signal` section, which is bound to `SignalOptions`. Any .NET configuration
source works: `appsettings.json`, environment variables (`Signal__Mode=JsonRpc`,
`Signal__Accounts__0=+49…`), user secrets, command-line arguments, and so on.

Durations use the `TimeSpan` format `hh:mm:ss`. Enum values are case-insensitive names
(`JsonRpc`, not `json-rpc`).

```json
{
  "Signal": {
    "BaseUrl": "http://localhost:8080",
    "Mode": "JsonRpc",
    "Accounts": [ "+4915112345678" ],
    "MaxConcurrency": 4,
    "VerifyModeOnStartup": true,
    "FailOnModeMismatch": false,
    "Receive":   { "PollingInterval": "00:00:01", "TimeoutSeconds": 1, "MaxMessages": null,
                   "IgnoreAttachments": false, "IgnoreStories": true, "SendReadReceipts": false,
                   "MaxErrorBackoff": "00:00:30" },
    "WebSocket": { "ReconnectMinDelay": "00:00:01", "ReconnectMaxDelay": "00:00:30",
                   "KeepAlive": "00:00:20", "ReceiveBufferSize": 16384 },
    "Http":      { "Timeout": "00:00:30", "RetryCount": 3 },
    "Commands":  { "Prefixes": [ "/", "!" ], "CaseSensitive": false, "RespondToUnknown": true,
                   "UnknownCommandMessage": "Unknown command '{0}'. Send {1}help for a list of commands.",
                   "SuggestSimilarCommands": true,
                   "ErrorMessage": "Sorry, something went wrong while executing this command.",
                   "QuoteReplies": false, "EnableHelp": true, "HelpPageSize": 20, "Admins": [ "+4915112345678" ] },
    "AccessControl": { "AllowedSenders": [], "BlockedSenders": [], "IgnoreOwnMessages": true },
    "RateLimit": { "PermitsPerWindow": 0, "Window": "00:01:00" },
    "Background": { "MaxConcurrency": 4, "Capacity": 100, "PromptTimeout": "00:02:00" },
    "Scheduler": { "PollInterval": "00:00:01", "RetryDelay": "00:01:00" }
  }
}
```

## Root

| Key | Type | Default | Validation / notes |
|---|---|---|---|
| `BaseUrl` | URL | `http://localhost:8080/` | Absolute `http`/`https` URL. A base path (reverse proxy) is supported. |
| `Mode` | `Normal` \| `Native` \| `JsonRpc` \| `JsonRpcNative` | `Normal` | Must match the container's `MODE`. See [Execution modes](execution-modes.md). |
| `Accounts` | string[] | – | **Required**, and each entry must be a valid E.164 number. One receive loop runs per account. |
| `MaxConcurrency` | int | 4 | ≥ 1. The number of conversations processed in parallel. |
| `VerifyModeOnStartup` | bool | true | Checks `/v1/about` on startup |
| `FailOnModeMismatch` | bool | false | Stop instead of warning on a mode mismatch |

## `Receive` (polling modes only)

| Key | Default | Validation / notes |
|---|---|---|
| `PollingInterval` | 1 s | > 0. The pause after an empty poll. |
| `TimeoutSeconds` | 1 | ≥ 0. Server-side long-poll duration. |
| `MaxMessages` | null | > 0 when set |
| `IgnoreAttachments` | false | Don't store attachments in the container |
| `IgnoreStories` | true | Skip stories. Set it to `false` to receive `StoryReceived` events. Applies in every mode: the polling modes pass it to the API, and the mapper also drops stories when it's `true`. |
| `SendReadReceipts` | false | The container sends read receipts automatically |
| `MaxErrorBackoff` | 30 s | Cap of the retry delay after failed polls |

## `WebSocket` (json-rpc modes only)

| Key | Default | Validation / notes |
|---|---|---|
| `ReconnectMinDelay` | 1 s | > 0 and ≤ `ReconnectMaxDelay` |
| `ReconnectMaxDelay` | 30 s | |
| `KeepAlive` | 20 s | Ping interval. Keeps idle connections open through proxies. |
| `ReceiveBufferSize` | 16384 | ≥ 1024 bytes. Larger messages are reassembled. |

## `Http`

| Key | Default | Validation / notes |
|---|---|---|
| `Timeout` | 30 s | > 0. **Polling modes:** it must exceed `Receive:TimeoutSeconds` by at least 5 s. |
| `RetryCount` | 3 | ≥ 0. Applies only to GET/PUT/DELETE. Sends are never retried. |

## `Commands`

| Key | Default | Notes |
|---|---|---|
| `Prefixes` | `["/"]` when empty | Non-empty strings. The longest match wins. |
| `CaseSensitive` | false | Applies to command names and aliases |
| `RespondToUnknown` | true | Reply to prefixed messages that don't match a command |
| `UnknownCommandMessage` | see above | `{0}` = the typed name, `{1}` = the prefix |
| `DisabledCommandMessage` | "This command is disabled in this conversation." | Reply when a command is disabled in the conversation's settings; empty for no reply |
| `SuggestSimilarCommands` | true | Append "Did you mean /help?" when a visible command, alias or group name (or, for `/group typo`, a subcommand) is one typo away, or two for names of 6+ characters. Swapped letters count as one typo. |
| `ErrorMessage` | see above | Sent when a command throws |
| `QuoteReplies` | false | Replies quote the triggering message |
| `EnableHelp` | true | Enables the built-in `help` command |
| `HelpPageSize` | 20 | Commands per `/help` page (`/help 2` for the next). 0 disables paging. |
| `Admins` | [] | Phone numbers or UUIDs allowed to use `[RequireAdmin]` commands. They also have the `admin` role. |
| `Roles` | {} | Role name → phone numbers or UUIDs for `[RequireRole]`, e.g. `{ "moderator": ["+4915112345678"] }`. Role names are case-insensitive; changes apply without restart. |

## `AccessControl`

| Key | Default | Notes |
|---|---|---|
| `AllowedSenders` | [] | If not empty, **only** these senders are processed |
| `BlockedSenders` | [] | Always ignored |
| `IgnoreOwnMessages` | true | Ignore messages from the receiving account itself, including transcripts of messages sent from its other devices. Set it to `false` to receive `MessageSent` events; transcripts never run commands. |

## `RateLimit`

| Key | Default | Notes |
|---|---|---|
| `PermitsPerWindow` | 0 (off) | Data messages per sender per window |
| `Window` | 1 min | > 0 |

## `Background`

Work queued with `RunInBackgroundAsync` / `IBackgroundWorkQueue`.

| Key | Default | Notes |
|---|---|---|
| `MaxConcurrency` | 4 | Work items running at the same time; at least 1 |
| `Capacity` | 100 | Items that may wait; queuing waits while the queue is full. At least 1. |
| `PromptTimeout` | 2 min | Default wait of `BackgroundWork.PromptAsync`; must be positive |

Items still queued at shutdown are dropped (logged as a warning), so don't use the queue for work that must survive restarts.

## `Scheduler`

Scheduled messages (`IMessageScheduler`, `ScheduleReplyAsync`).

| Key | Default | Notes |
|---|---|---|
| `PollInterval` | 1 s | How often due messages are checked for, which is the send-time precision. With a database store, a longer interval means fewer queries. |
| `RetryDelay` | 1 min | Wait before retrying a message whose send failed |

After downtime, overdue one-off messages are sent on the first check. A recurring message sends one catch-up and then continues at its next future time; it doesn't replay every missed occurrence.

## Hot reload

Options read through `IOptionsMonitor` take effect without a restart when the configuration file changes:

- `Commands:*` (the `CaseSensitive` flag and the command table are fixed once built);
- `AccessControl:*`;
- `RateLimit:*`;
- `Receive:*`;
- the `WebSocket` reconnect settings.

`BaseUrl`, `Mode`, `Accounts`, `MaxConcurrency` and `Http:*` are read once at startup.
