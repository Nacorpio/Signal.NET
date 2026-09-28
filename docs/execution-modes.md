# Execution modes

signal-cli-rest-api can run signal-cli in four ways, selected by the container's `MODE` environment
variable. Signal.NET supports all four. `Signal:Mode` must be set to the same value.

| Container `MODE` | `Signal:Mode` | How signal-cli runs | Receiving in Signal.NET | Latency | Resource use |
|---|---|---|---|---|---|
| `normal` | `Normal` | A new JVM process per request | `PollingMessageReceiver` (HTTP) | High (JVM start per call) | Low when idle |
| `native` | `Native` | A new GraalVM native process per request | `PollingMessageReceiver` (HTTP) | Medium | Low when idle |
| `json-rpc` | `JsonRpc` | One long-running daemon (JVM) | `WebSocketMessageReceiver` (push) | **Lowest** | Constant JVM memory |
| `json-rpc-native` | `JsonRpcNative` | One long-running native daemon | `WebSocketMessageReceiver` (push) | Low | Lower memory than JVM |

**Recommendation:** use `json-rpc` for bots. Messages arrive instantly over the WebSocket and sends
avoid the per-request start-up cost of signal-cli.

## What changes and what doesn't

- **Changes:** only `GET /v1/receive/{number}`. In `normal`/`native` it is a regular HTTP endpoint that returns pending envelopes. In `json-rpc*` it upgrades to a WebSocket and pushes envelopes as they arrive, and plain HTTP polling of it is not supported.
- **Unchanged:** sending and every other endpoint. Your commands and handlers behave the same in all modes.

## How Signal.NET selects the transport

1. `AddSignalInfrastructure()` registers the receivers as keyed services, keyed by `ExecutionMode`.
2. `SignalHostedService` asks `IMessageReceiverFactory.Create(Signal:Mode)` for the receiver.
3. `ExecutionMode.IsStreaming` (a C# 14 extension property) states the rule in code: the `json-rpc*` modes stream.

Replace the transport for one or all modes with `UseReceiver<T>(modes)`.

## Detecting a mismatch

With `VerifyModeOnStartup` (the default), the host reads `/v1/about` on startup:

```
info: Connected to signal-cli-rest-api 0.100 (build 2, mode json-rpc)
warn: Signal:Mode is Normal (normal) but the container runs in 'json-rpc' mode.
```

Set `FailOnModeMismatch: true` to stop the host instead of only warning. Typical symptoms of a mismatch
if you ignore the warning:

- **Polling a json-rpc container:** HTTP errors on `/v1/receive`.
- **WebSocket against a normal container:** the connection is refused and retried forever.

## Switching modes

1. Change `MODE` in `docker-compose.yml` (or run `SIGNAL_MODE=json-rpc docker compose up -d`).
2. Change `Signal:Mode` in `appsettings.json`.
3. Restart both. The account data in the mounted volume is shared by all modes.
