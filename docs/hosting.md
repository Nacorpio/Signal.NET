# Hosting layer (`Signal.Hosting`)

**Purpose:** run Signal.NET inside the .NET Generic Host (console worker, ASP.NET Core app, Windows
service, container). It composes the lower layers and owns the long-running receive loop.

| Component | Purpose |
|---|---|
| `SignalServiceCollectionExtensions` | The `AddSignal(...)` overloads and `AddSignalApi()` for health checks |
| `ISignalBuilder` / `SignalBuilder` | The fluent extension API |
| `SignalHostedService` | The `BackgroundService` that receives and processes messages |
| `SignalApiHealthCheck` | `IHealthCheck` backed by `GET /v1/health` |

## `AddSignal`

```csharp
builder.AddSignal();                                               // IHostApplicationBuilder, reads "Signal"
services.AddSignal(configuration.GetSection("Signal"));            // explicit section
services.AddSignal(o => { o.Accounts = ["+49…"]; o.Mode = ExecutionMode.JsonRpc; });   // code only
```

Every overload does the following:

1. Registers `SignalOptions` with `ValidateOnStart()`.
2. Calls `AddSignalApplication()` and `AddSignalInfrastructure()`.
3. Adds `SignalHostedService`.
4. Returns an `ISignalBuilder`.

Calling `AddSignal` more than once is safe, because the framework services use `TryAdd`.

## `ISignalBuilder`

| Method | Purpose |
|---|---|
| `AddCommands(assembly)` | Registers every `ICommand` (as scoped) and `CommandModule` in the assembly |
| `AddCommand<T>()` / `AddCommandModule<T>()` | Registers a single command or module |
| `MapCommand(name, handler, description, aliases)` | Registers a lambda command |
| `AddMiddleware<T>()` | Adds a pipeline step after the built-in guards |
| `AddEventHandler<TEvent, THandler>()` / `AddEventHandlers(assembly)` | Registers event handlers (scoped). A class may handle several events. |
| `AddArgumentConverter<T>()` | Registers a custom parameter type conversion |
| `UseReceiver<T>(modes)` | Replaces the receiver for the given modes, or for all modes |
| `Configure(Action<SignalOptions>)` | Code-based option overrides, applied after configuration binding |
| `Services` | Escape hatch for anything else |

## `SignalHostedService`

**Purpose:** the engine. `ExecuteAsync` runs these steps:

1. **Verify the mode** (if `VerifyModeOnStartup` is on). It calls `/v1/about` and compares the reported mode with `Signal:Mode`:
   - on a mismatch it logs a warning, or throws if `FailOnModeMismatch` is set, which stops the host;
   - if the API is unreachable it logs a warning and continues, because the receivers retry anyway.
2. **Create partitions:** `MaxConcurrency` bounded channels (64 slots each), each with a single worker.
3. **Receive:** one loop per account. An envelope that answers a pending prompt (`IPromptRegistry.TryDeliver`) goes
   straight to the waiting work and is not processed further. That has to happen here, because the answer must not
   queue behind the work waiting for it. Every other envelope goes to partition `hash(conversation) % MaxConcurrency`. If a receiver fails unexpectedly, the loop logs it and restarts after 5 s.
4. **Process:** each worker takes envelopes one at a time. For each it creates a new async DI scope, builds a `MessageContext`, and runs the `IMessagePipeline`. Exceptions are logged and never stop the worker.
5. **Shut down:** when the host stops, the receive loops end, the channels are completed, and the workers drain.

**Ordering guarantee:** all messages of one conversation land in the same partition and are therefore
processed strictly in order. Different conversations are processed in parallel.

### `BackgroundWorkService`

**Purpose:** runs the `IBackgroundWorkProcessor`, so work queued with `RunInBackgroundAsync` /
`IBackgroundWorkQueue` executes **outside** the partitions. `Background:MaxConcurrency` workers each take one
item at a time, in its own async DI scope. Failures are logged and never stop a worker. On shutdown, running
work sees its cancellation token cancelled, and items still queued are dropped with a warning.

## Health checks

```csharp
builder.Services.AddHealthChecks().AddSignalApi();          // name "signal-api", tag "signal"
app.MapHealthChecks("/health");                             // ASP.NET Core
```

`SignalApiHealthCheck` reports Healthy when `/v1/health` succeeds. Otherwise it reports the registration's
`FailureStatus`.

## Hosting inside ASP.NET Core

`AddSignal` works with `WebApplication.CreateBuilder(args)` in the same way, so a web API and the bot can
share one process and DI container. An HTTP endpoint could, for example, inject `IMessageSender` to send
notifications.
