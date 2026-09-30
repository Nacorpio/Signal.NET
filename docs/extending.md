# Extensibility guide

Signal.NET is built around dependency injection. Every framework service is registered with
`TryAdd*`, so **you can replace anything by registering your own implementation before `AddSignal()`**.
The builder methods below cover the common extension points.

| I want to… | Extension point | Lifetime |
|---|---|---|
| Add a command | `CommandModule`, `ICommand`/`CommandBase`, `MapCommand` | per execution / scoped |
| Keep per-conversation settings (prefixes, disabled commands, culture) | Implement `IConversationSettingsStore` and register it before `AddSignal` | singleton |
| Grant roles from your own data (e.g. a database) | Implement `IRoleProvider` and call `AddRoleProvider<T>()` | scoped (default) |
| Keep scheduled messages across restarts | Implement `IScheduledMessageStore` and register it before `AddSignal` | singleton |
| Support a custom argument type | Implement `IParsable<T>` on the type, or `ArgumentConverter<T>` + `AddArgumentConverter<T>()` | singleton |
| Add a permission check | Derive from `PreconditionAttribute` | attribute |
| Change error or unknown-command replies | Replace `ICommandResultHandler` | scoped |
| Run logic for every message | `IMessageMiddleware` + `AddMiddleware<T>()` | scoped |
| React to events | `IEventHandler<TEvent>` + `AddEventHandler`/`AddEventHandlers` | scoped |
| Use a different transport | Derive from `ChannelMessageReceiver` (or implement `IMessageReceiver`) + `UseReceiver<T>()` | transient |
| Customize WebSocket connections | Replace `IWebSocketConnector` | singleton |
| Share cooldowns or rate limits across instances | Replace `ICooldownTracker` / `ISenderRateLimiter` | singleton |
| Fake Signal in tests | Replace any port (`IMessageSender`, `IGroupService`, …) | any |

## Examples

### Custom argument type

The simplest way is to make the type parseable. Signal.NET picks it up automatically:

```csharp
public readonly record struct Hex(int Value) : IParsable<Hex>
{
    public static Hex Parse(string s, IFormatProvider? p) => new(Convert.ToInt32(s, 16));
    public static bool TryParse(string? s, IFormatProvider? p, out Hex r)
    {
        r = default;
        try { r = Parse(s!, p); return true; } catch { return false; }
    }
}

[Command("color")] public Task ColorAsync(Hex rgb) => ReplyAsync($"#{rgb.Value:x6}");
```

For types you don't own, or when the conversion needs context (for example resolving a mention to its author):

```csharp
public sealed class MentionedUserConverter : ArgumentConverter<Recipient>
{
    public override string DisplayName => "mention or phone number";

    public override bool TryConvert(string input, CommandContext ctx, out Recipient value)
    {
        // Signal replaces mentions with U+FFFC in the text; take the author of the first mention.
        if (input is [(char)0xFFFC] && ctx.Data.Mentions.FirstOrDefault() is { } m)
        {
            value = Recipient.Parse(m.Author);
            return true;
        }

        return Recipient.TryParse(input, out value);
    }
}

builder.AddSignal().AddArgumentConverter<MentionedUserConverter>();   // overrides the built-in Recipient converter
```

### Custom precondition

```csharp
public sealed class RequireContactAttribute : PreconditionAttribute
{
    public override async ValueTask<PreconditionResult> CheckAsync(CommandContext ctx, CancellationToken ct)
    {
        var contacts = await ctx.Services.GetRequiredService<IContactService>().ListAsync(ctx.Account, ct);
        return contacts.Any(c => c.Number == ctx.Sender.Number?.Value)
            ? PreconditionResult.Success
            : Fail("Only contacts may use this command.");
    }
}
```

### Middleware: typing indicator while commands run

```csharp
public sealed class TypingMiddleware(ITypingIndicatorService typing, ICommandParser parser) : IMessageMiddleware
{
    public async Task InvokeAsync(MessageContext ctx, MessageDelegate next)
    {
        if (!parser.TryParse(ctx.Envelope.Data?.Text, out _))
        {
            await next(ctx);
            return;
        }

        await typing.StartTypingAsync(ctx.Account, ctx.Conversation, ctx.CancellationToken);
        try { await next(ctx); }
        finally { await typing.StopTypingAsync(ctx.Account, ctx.Conversation, CancellationToken.None); }
    }
}

builder.AddSignal().AddMiddleware<TypingMiddleware>();
```

### Localised command replies

```csharp
public sealed class GermanResultHandler : ICommandResultHandler
{
    // CommandResult is a union: the switch is exhaustive, and each case exposes only its own data.
    public Task HandleAsync(MessageContext ctx, CommandResult result) => result switch
    {
        CommandNotFound notFound      => ctx.ReplyAsync($"Unbekannter Befehl '{notFound.Parsed.Name}'."),
        CommandBindingFailed failed   => ctx.ReplyAsync($"{failed.Error}\nVerwendung: {failed.Command.FormatUsage(failed.Parsed.Prefix)}"),
        CommandPreconditionFailed { Reason: { } reason } => ctx.ReplyAsync(reason),
        CommandFaulted                => ctx.ReplyAsync("Da ist etwas schiefgelaufen."),
        CommandSucceeded or CommandPreconditionFailed or null => Task.CompletedTask,
    };
}

builder.Services.AddScoped<ICommandResultHandler, GermanResultHandler>();   // before AddSignal()
builder.AddSignal();
```

### Custom transport

```csharp
public sealed class QueueReceiver(IMyQueue queue) : ChannelMessageReceiver
{
    protected override async Task ProduceAsync(PhoneNumber account, ChannelWriter<IncomingEnvelope> writer, CancellationToken ct)
    {
        await foreach (var item in queue.ReadAsync(ct))
        {
            await writer.WriteAsync(item.ToEnvelope(account), ct);
        }
    }
}

builder.AddSignal().UseReceiver<QueueReceiver>();          // all modes
```

## Replacing a framework service

Because of `TryAdd`, a registration made **before** `AddSignal()` wins:

```csharp
builder.Services.AddSingleton<ICooldownTracker, RedisCooldownTracker>();
builder.AddSignal();
```

Services registered as enumerables (`IArgumentConverter`, `IEventHandler<T>`) are added to the built-in
set instead of replacing it. For argument converters, the **last** registration for a type wins.
