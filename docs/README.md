# Signal.NET documentation

Signal.NET is a .NET 11 / C# 15 framework for building Signal bots and integrations on top of the
[signal-cli-rest-api](https://github.com/bbernhard/signal-cli-rest-api) Docker container.
This folder describes every component, what it is for, how it behaves, and how to extend it.

## Contents

| Document | What it covers |
|---|---|
| [Architecture](architecture.md) | Layers, dependency rules, and the path of a message from the container to your code |
| [Domain layer](domain.md) | Value objects, the envelope aggregate, the outgoing message builder, entities, domain events, execution modes |
| [Application layer](application.md) | Ports, the `ISignalClient` facade, options and validation, the message pipeline, built-in middleware, event dispatching |
| [Command system](commands.md) | Defining commands, parsing, argument binding, preconditions, the registry, execution, results, help |
| [Infrastructure layer](infrastructure.md) | REST client, JSON contracts, resilience, envelope mapping, REST adapters, polling and WebSocket receivers |
| [Hosting layer](hosting.md) | `AddSignal`, `ISignalBuilder`, the background receiver service, the health check |
| [Configuration reference](configuration.md) | Every `appsettings.json` key, its default, and its validation rule |
| [Execution modes](execution-modes.md) | `normal`, `native`, `json-rpc`, `json-rpc-native` and how Signal.NET handles each |
| [Extensibility guide](extending.md) | Every extension point, with examples |
| [Sample bot and testing](samples-and-testing.md) | The sample project, Docker setup, and the test suites |

## Five-minute tour

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddSignal()                               // reads the "Signal" section of appsettings.json
    .AddCommands(typeof(Program).Assembly)        // registers commands found in this assembly
    .AddEventHandlers(typeof(Program).Assembly);  // registers event handlers found in this assembly

await builder.Build().RunAsync();
```

```csharp
public sealed class DiceModule : CommandModule
{
    [Command("roll", Description = "Rolls a die"), Cooldown(5)]
    public Task RollAsync(int sides = 6) => ReplyAsync($"🎲 {Random.Shared.Next(1, sides + 1)}");
}
```

A user sends `/roll 20`. The container delivers the message and Signal.NET parses it, checks the
cooldown, binds `20` to `sides`, and runs the method. The method's reply goes back into the same
conversation, whether that is a direct message or a group.

## Glossary

| Term | Meaning |
|---|---|
| **Account** | The Signal phone number registered or linked in the container. The bot sends and receives as this number. |
| **Envelope** | One unit received from Signal: a data message, receipt, or typing indicator, plus its sender and timestamp. |
| **Conversation** | Where a reply goes: the group for group messages, otherwise the sender. |
| **Port** | An interface defined by the application layer and implemented by infrastructure, such as `IMessageSender`. |
| **Adapter** | An infrastructure class implementing a port on top of the REST API. |
| **Execution mode** | The container's `MODE` variable. It decides whether messages are polled over HTTP or pushed over a WebSocket. |
| **Middleware** | A step in the per-message pipeline, like ASP.NET Core middleware. |
| **Precondition** | An attribute that must pass before a command runs, such as `[RequireAdmin]`. |
