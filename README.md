# Signal.NET

A .NET 11 / C# 15 framework for building Signal bots and integrations on top of
[bbernhard/signal-cli-rest-api](https://github.com/bbernhard/signal-cli-rest-api)
([Swagger](https://bbernhard.github.io/signal-cli-rest-api/)).

- **DDD layering** – pure domain model, application ports, infrastructure adapters, hosting.
- **DI everywhere** – every service is registered with `TryAdd*` and can be replaced.
- **Text command system** – class-based or attribute-based commands, typed argument binding, flags, preconditions, cooldowns, generated help.
- **`appsettings.json` configuration** – validated on startup, hot-reloadable command/access settings.
- **All four container modes** – `normal`, `native` (HTTP polling) and `json-rpc`, `json-rpc-native` (WebSocket push).

## Documentation

The full documentation is in [`docs/`](docs/README.md): [architecture](docs/architecture.md), [domain](docs/domain.md), [application](docs/application.md), the [command system](docs/commands.md), [infrastructure](docs/infrastructure.md), [hosting](docs/hosting.md), the [configuration reference](docs/configuration.md), [execution modes](docs/execution-modes.md), [extensibility](docs/extending.md), and [samples and testing](docs/samples-and-testing.md).

Every public type and member also has XML documentation comments, which IntelliSense shows. Any public member without a comment fails the build.

## Solution layout

| Project | Responsibility |
|---|---|
| `Signal.Domain` | Value objects (`PhoneNumber`, `GroupId`, `Recipient`), `IncomingEnvelope` aggregate, `OutgoingMessage` builder, entities (`Group`, `Contact`, `Identity`), domain events, `ExecutionMode`. No dependencies. |
| `Signal.Application` | Ports (`IMessageSender`, `IMessageReceiver`, `IGroupService`, ...), `SignalOptions`, message middleware pipeline, domain event dispatcher, command system. |
| `Signal.Infrastructure` | REST adapters (typed `HttpClient`, source-generated JSON, standard resilience pipeline), envelope mapping, polling and WebSocket receivers keyed by `ExecutionMode`. |
| `Signal.Hosting` | `AddSignal()` + `ISignalBuilder`, `SignalHostedService` (receive loops + partitioned processing), health check. |
| `samples/Signal.Sample.Bot` | Worker service demonstrating commands, event handlers and configuration. |

### Message flow

```
signal-cli-rest-api ──(poll | WebSocket)──► IMessageReceiver ──► partition by conversation (N workers)
    ──► new DI scope ──► ExceptionHandling ► Logging ► AccessControl ► RateLimiting ► [your middleware]
    ──► DomainEventMiddleware (IEventHandler<T>) ► CommandMiddleware (parse ► preconditions ► bind ► execute)
```

Messages from different conversations are processed in parallel (`MaxConcurrency`), while messages from the same conversation keep their order.

## Requirements

- The .NET 11 SDK preview pinned in [`global.json`](global.json). C# 15 preview features (unions, extension members) are used.
- Docker, to run signal-cli-rest-api
- A Signal account to link, or a phone number to register

## Quick start

1. Start the container. `MODE` must match `Signal:Mode`:

   ```bash
   SIGNAL_MODE=json-rpc docker compose up -d
   ```

2. Link the container to your Signal account. Open `http://localhost:8080/v1/qrcodelink?device_name=signal-net` and scan the QR code in the Signal app (Settings → Linked devices). You can also register a new number through `/v1/register`.

3. Configure your number with user secrets, so it never ends up in the repository. The placeholder values in `appsettings.json` are overridden.

   ```bash
   dotnet user-secrets --project samples/Signal.Sample.Bot set "Signal:Accounts:0" "+4915112345678"
   dotnet user-secrets --project samples/Signal.Sample.Bot set "Signal:Commands:Admins:0" "+4915112345678"
   ```

   Run the bot:

   ```bash
   dotnet run --project samples/Signal.Sample.Bot
   ```

4. Send `/help` to the account from another device.

| `MODE` (container) | `Signal:Mode` | Receiving |
|---|---|---|
| `normal` | `Normal` | HTTP polling of `GET /v1/receive/{number}` |
| `native` | `Native` | HTTP polling |
| `json-rpc` | `JsonRpc` | WebSocket `ws://…/v1/receive/{number}` with auto-reconnect |
| `json-rpc-native` | `JsonRpcNative` | WebSocket |

With `VerifyModeOnStartup` enabled, the host reads `/v1/about` on startup and warns about a mode mismatch. With `FailOnModeMismatch` it stops instead.

## Usage

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddSignal()                                  // binds the "Signal" section
    .AddCommands(typeof(Program).Assembly)           // ICommand classes + CommandModules
    .AddEventHandlers(typeof(Program).Assembly)      // IEventHandler<TEvent>
    .AddMiddleware<MyMiddleware>()
    .AddArgumentConverter<MyTypeConverter>()
    .MapCommand("about", ctx => ctx.ReplyAsync("Signal.NET bot"));

builder.Services.AddHealthChecks().AddSignalApi();
await builder.Build().RunAsync();
```

### Commands

Attribute-based modules support typed parameters, flags, remainder text, constructor injection and preconditions:

```csharp
[RequireGroup]
public sealed class GroupModule(IGroupService groups) : CommandModule
{
    [Command("kick", Description = "Removes a member")]
    [RequireAdmin, RequireGroupAdmin]
    public async Task KickAsync(PhoneNumber member, CancellationToken ct)
    {
        await groups.RemoveMembersAsync(Context.Account, Context.Group!.Value, [member.Value], ct);
        await ReplyAsync($"Removed {member}.");
    }

    [Command("echo"), Cooldown(5)]
    public Task EchoAsync([Remainder] string text, [Flag] bool upper) =>
        ReplyAsync(upper ? text.ToUpperInvariant() : text);   // /echo --upper hello world
}
```

Class-based commands:

```csharp
[Command("ping", Aliases = ["p"], Description = "Checks that the bot is alive.")]
public sealed class PingCommand : CommandBase
{
    public override Task ExecuteAsync(CommandContext context, CancellationToken ct) =>
        context.ReplyAsync("pong", cancellationToken: ct);
}
```

- **Parsing:** you can configure several prefixes. Quoted arguments work with `"…"`, `'…'` and the typographic quotes that mobile keyboards insert. Flags take the form `--name value`, `--name=value` or `--switch`, and `--` ends flag parsing.
- **Binding:** `string`, `bool` (yes/no/on/off), enums, `Recipient`, and anything that implements `IParsable<T>` (numbers, `Guid`, `TimeSpan`, `PhoneNumber`, `GroupId`, ...) bind automatically. Nullable and optional parameters are supported. If binding fails, the bot replies with the reason and the usage line.
- **Preconditions:** the built-ins are `RequireAdmin`, `RequireGroup`, `RequireDirectMessage`, `RequireGroupAdmin` and `Cooldown` (scoped per sender, per conversation or globally). For your own, derive from `PreconditionAttribute`.
- **Results:** replace `ICommandResultHandler` to change how unknown commands and errors are reported.

### Events

```csharp
public sealed class Welcome(IMessageSender sender) : IEventHandler<GroupUpdated>
{
    public Task HandleAsync(GroupUpdated e, CancellationToken ct) =>
        sender.SendAsync(e.Envelope.Account, OutgoingMessage.To(e.Group).WithText("👋").Build(), ct);
}
```

The available events are `MessageReceived`, `ReactionReceived`, `ReceiptReceived`, `TypingIndicatorChanged` and `GroupUpdated`.

### Using the API directly

Inject `ISignalClient` (a facade) or a single port:

```csharp
await signal.Messages.SendAsync(account, OutgoingMessage.To(recipient)
    .WithStyledText("**bold** and *italic*")
    .WithAttachment(bytes, "image/png", "chart.png")
    .Build());
await signal.Groups.CreateAsync(account, "Team", ["+4915112345678"]);
```

## Configuration reference (`Signal` section)

| Key | Default | Notes |
|---|---|---|
| `BaseUrl` | `http://localhost:8080/` | |
| `Mode` | `Normal` | `Normal`, `Native`, `JsonRpc`, `JsonRpcNative` |
| `Accounts` | – | Required. E.164 numbers to receive for. |
| `MaxConcurrency` | `4` | Parallel conversations. |
| `VerifyModeOnStartup` / `FailOnModeMismatch` | `true` / `false` | |
| `Receive:*` | 1 s interval, 1 s timeout | Polling modes only: `PollingInterval`, `TimeoutSeconds`, `MaxMessages`, `IgnoreAttachments`, `IgnoreStories`, `SendReadReceipts`, `MaxErrorBackoff`. |
| `WebSocket:*` | 1 s–30 s backoff, 20 s keep-alive | WebSocket modes only. |
| `Http:Timeout` / `Http:RetryCount` | 30 s / 3 | Only GET/PUT/DELETE requests are retried. Sends are never retried, so a message is not duplicated. |
| `Commands:*` | prefix `/` | `Prefixes`, `CaseSensitive`, `RespondToUnknown`, `UnknownCommandMessage`, `ErrorMessage`, `QuoteReplies`, `EnableHelp`, `Admins`. |
| `AccessControl:*` | | `AllowedSenders`, `BlockedSenders`, `IgnoreOwnMessages`. |
| `RateLimit:*` | disabled | `PermitsPerWindow` per sender per `Window`. |

The options are validated on startup (`SignalOptionsValidator`). An invalid configuration stops the host with a list of all problems.

## Extending

| Want to… | Do |
|---|---|
| Add behaviour to every message | `AddMiddleware<T>()` (`IMessageMiddleware`) |
| React to events | `IEventHandler<TEvent>` |
| Bind a custom argument type | Implement `IParsable<T>`, or `AddArgumentConverter<T>()` |
| Swap the transport or add a custom mode receiver | `UseReceiver<T>(modes)` or register a keyed `IMessageReceiver` |
| Customize WebSocket connections (proxy, TLS, auth) | Replace `IWebSocketConnector` |
| Replace any port (e.g. for tests) | Register your implementation before or after `AddSignal()` |

## Tests

```bash
dotnet test
```

The tests cover the domain invariants, the parser, binding and preconditions, and the pipeline. On the infrastructure side they cover REST request bodies, the retry policy, envelope mapping, polling, and a real WebSocket over loopback, including reconnects.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the build setup and conventions, and [SECURITY.md](SECURITY.md) for how to report vulnerabilities and run a bot safely. Changes are tracked in [CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE) © 2026 Nacorpio

Signal.NET is an independent project. It is not affiliated with Signal Messenger LLC or signal-cli.
