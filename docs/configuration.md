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
                   "ErrorMessage": "Sorry, something went wrong while executing this command.",
                   "QuoteReplies": false, "EnableHelp": true, "Admins": [ "+4915112345678" ] },
    "AccessControl": { "AllowedSenders": [], "BlockedSenders": [], "IgnoreOwnMessages": true },
    "RateLimit": { "PermitsPerWindow": 0, "Window": "00:01:00" }
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
| `ErrorMessage` | see above | Sent when a command throws |
| `QuoteReplies` | false | Replies quote the triggering message |
| `EnableHelp` | true | Enables the built-in `help` command |
| `Admins` | [] | Phone numbers or UUIDs allowed to use `[RequireAdmin]` commands |

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

## Hot reload

Options read through `IOptionsMonitor` take effect without a restart when the configuration file changes:

- `Commands:*` (the `CaseSensitive` flag and the command table are fixed once built);
- `AccessControl:*`;
- `RateLimit:*`;
- `Receive:*`;
- the `WebSocket` reconnect settings.

`BaseUrl`, `Mode`, `Accounts`, `MaxConcurrency` and `Http:*` are read once at startup.
