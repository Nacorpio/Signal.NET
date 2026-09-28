# Sample bot and testing

## Sample bot (`samples/Signal.Sample.Bot`)

**Purpose:** a runnable reference that shows every way to extend Signal.NET.

| File | Demonstrates |
|---|---|
| `Program.cs` | `AddSignal()`, assembly scanning, `MapCommand`, the health check |
| `Commands/PingCommand.cs` | Class-based command (`CommandBase` + `[Command]`), constructor injection (`TimeProvider`), reactions |
| `Commands/UtilityModule.cs` | Module commands: `[Remainder]`, `[Flag]` switch, optional parameter, `[Cooldown]`, styled replies |
| `Commands/GroupModule.cs` | Module-level `[RequireGroup]`, `[RequireAdmin]` + `[RequireGroupAdmin]`, injecting `IGroupService`, binding a `PhoneNumber`, a hidden command |
| `Handlers/EventHandlers.cs` | `IEventHandler<ReactionReceived>` (logging), `IEventHandler<GroupUpdated>` (welcome message) |
| `appsettings.json` | A complete configuration |

### Running it

```bash
SIGNAL_MODE=json-rpc docker compose up -d                 # start signal-cli-rest-api
# link a device: open http://localhost:8080/v1/qrcodelink?device_name=signal-net and scan it in Signal
# set Signal:Accounts and Signal:Commands:Admins in appsettings.json
dotnet run --project samples/Signal.Sample.Bot
```

Try `/help`, `/ping`, `/echo --upper hello`, `/roll 20`, `/bold text`, `/about` and, in a group, `/groupinfo`.

### `docker-compose.yml`

This runs `bbernhard/signal-cli-rest-api` on port 8080. `MODE` comes from the `SIGNAL_MODE` variable
(default `json-rpc`), and account data is persisted in `./signal-cli-config` (git-ignored).

## Test suites (`tests/`)

Run everything with `dotnet test`. No container is needed.

| Project | Focus | Technique |
|---|---|---|
| `Signal.Domain.Tests` | Value object parsing and normalisation, `GroupId` conversion, recipient detection, `OutgoingMessage` invariants, envelope → event derivation, `ExecutionMode` properties | Plain unit tests |
| `Signal.Application.Tests` | Tokenizer and parser, argument binding and all error messages, preconditions, cooldowns, help, unknown commands, faulted commands, delegate and class commands, duplicate names, events, access control, rate limit, options validation | `TestHarness`: the real application layer wired to an in-memory `FakeSignal` that implements the ports |
| `Signal.Infrastructure.Tests` | Envelope mapping from realistic signal-cli JSON, send request bodies (snake_case), no retries for POST and retries for GET, API error translation, path escaping, `/v1/about` parsing, receiver selection per mode, polling query and error recovery, WebSocket frame reassembly and reconnects | `StubHandler` (a recording `HttpMessageHandler`) plugged into the real `HttpClient` + resilience pipeline; a real WebSocket over a loopback `TcpListener` via a custom `IWebSocketConnector` |

### Writing your own tests

Test commands without Signal by reusing the harness pattern:

```csharp
var services = new ServiceCollection();
services.AddLogging();
services.AddOptions<SignalOptions>().Configure(o => o.Accounts = ["+15550000000"]);
services.AddSignalApplication();
services.AddSingleton<IMessageSender, RecordingSender>();          // your fake
services.GetOrAddSingletonInstance<CommandCatalog>().AddModule(typeof(MyModule));

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var envelope = new IncomingEnvelope(
    PhoneNumber.Parse("+15550000000"),
    new Sender(PhoneNumber.Parse("+15550001111"), null, "Test"),
    Timestamp: 1,
    Content: new DataMessage(1, "/mycommand 42"));   // DataMessage converts implicitly to the EnvelopeContent union
await provider.GetRequiredService<IMessagePipeline>().ExecuteAsync(new MessageContext(envelope, scope.ServiceProvider, default));
// assert on RecordingSender
```

### Documentation checks

The library projects build with `GenerateDocumentationFile` enabled (see `src/Directory.Build.props`),
and warnings are treated as errors. **A public member without an XML comment fails the build**, which
keeps the API reference complete. The generated `Signal.*.xml` files ship next to the DLLs and power
IntelliSense.
