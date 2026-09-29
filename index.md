---
_layout: landing
---

# Signal.NET

A .NET 11 / C# 15 framework for building Signal bots on top of
[signal-cli-rest-api](https://github.com/bbernhard/signal-cli-rest-api).

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddSignal()                               // reads the "Signal" section of appsettings.json
    .AddCommands(typeof(Program).Assembly);

await builder.Build().RunAsync();

public sealed class DiceModule : CommandModule
{
    [Command("roll", Description = "Rolls a die"), Cooldown(5)]
    public Task RollAsync(int sides = 6) => ReplyAsync($"🎲 {Random.Shared.Next(1, sides + 1)}");
}
```

## Features

- **DDD layering with dependency injection.** Every service can be replaced.
- **Text command system.** Typed arguments, flags, preconditions, cooldowns and generated help.
- **All four container modes.** HTTP polling (`normal`, `native`) and WebSocket push (`json-rpc`, `json-rpc-native`).
- **C# 15 unions.** Exhaustive pattern matching over recipients, envelope content and command results.
- **`appsettings.json` configuration.** Validated when the host starts.

## Where to go next

- [Architecture](docs/architecture.md): how the layers fit together and how a message flows through them
- [Command system](docs/commands.md): writing commands
- [Configuration reference](docs/configuration.md): every setting
- [API reference](api/index.md): every public type and member
- [Roadmap](ROADMAP.md): what's planned
