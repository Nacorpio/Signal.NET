# Command system (`Signal.Application.Commands`)

**Purpose:** turn text such as `/remind --in 00:10:00 "buy milk"` into a typed method call, with
validation, permissions, cooldowns, generated help and user-friendly error messages.

```
text ─► ICommandParser ─► ParsedCommand ─► ICommandRegistry (lookup by name or alias)
     ─► PreconditionAttribute[] (ordered) ─► ICommandArgumentBinder (IArgumentConverter per parameter)
     ─► CommandDescriptor.Executor ─► CommandResult ─► ICommandResultHandler (reply to user)
```

## Three ways to define commands

### 1. Module methods (recommended)

```csharp
[RequireGroup]                                         // applies to every command in the module
public sealed class ModerationModule(IGroupService groups) : CommandModule
{
    [Command("kick", Aliases = ["remove"], Description = "Removes a member")]
    [RequireGroupAdmin]
    public async Task KickAsync(
        [Summary("Phone number of the member")] PhoneNumber member,
        [Remainder] string? reason = null,
        CancellationToken ct = default)
    {
        await groups.RemoveMembersAsync(Context.Account, Context.Group!.Value, [member.Value], ct);
        await ReplyAsync(reason is null ? $"Removed {member}." : $"Removed {member}: {reason}");
    }
}
```

- **Creation:** a new module instance is created for every execution, with constructor injection from the message scope. It is disposed afterwards if it implements `IDisposable` or `IAsyncDisposable`.
- **Return types:** methods may return `void`, `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`.
- **Injected parameters:** parameters of type `CommandContext` and `CancellationToken` are injected, not bound from the text.
- **Helpers:** `Context`, `ReplyAsync(text, quote?)` and `ReactAsync(emoji)`.

### 2. Class-based commands

```csharp
[Command("ping", Aliases = ["p"], Description = "Checks that the bot is alive")]
public sealed class PingCommand(TimeProvider time) : CommandBase
{
    public override Task ExecuteAsync(CommandContext context, CancellationToken ct) =>
        context.ReplyAsync($"pong ({(time.GetUtcNow() - context.Envelope.ReceivedAt).TotalMilliseconds:0} ms)", cancellationToken: ct);
}
```

- `ICommand` is the contract. `CommandBase` reads `Name`, `Aliases`, `Description`, `Usage` and `Hidden` from `[Command]`, and you can override each of them.
- Instances are resolved from the message scope (registered as scoped).
- Arguments are **not** bound. Read `context.Arguments` and `context.Flags`.

### 3. Delegate commands

```csharp
builder.AddSignal().MapCommand("about", ctx => ctx.ReplyAsync("Signal.NET bot"), "Shows bot info", "info");
```

Best for one-liners. Arguments are available through `ctx.Arguments` and `ctx.Flags`.

## Components

### Attributes (`Attributes.cs`)

| Attribute | Target | Purpose |
|---|---|---|
| `[Command(name)]` | method / class | Declares a command. Properties: `Aliases`, `Description`, `Usage`, `Hidden`. |
| `[Remainder]` | `string` parameter | Takes the rest of the text verbatim. Must be the last positional parameter. |
| `[Flag(name?)]` | parameter | Binds from `--name value` or `--name=value`. A `bool` flag is a switch (`--name`). |
| `[Summary(text)]` | parameter | Parameter description shown by `/help <command>` |

### Parsing (`Parsing/CommandParser.cs`)

| Type | Purpose |
|---|---|
| `ICommandParser` / `CommandParser` | Recognises `<prefix><name> <arguments>`. The longest matching configured prefix wins. `/ ping` (with a space) is not a command. |
| `ParsedCommand` | `Prefix`, `Name`, `RawArguments`, `Tokens`, plus naive `Arguments` and `Flags`, and `Split(switches)` |
| `CommandTokenizer` | Splits on whitespace (details below) |
| `CommandToken` | `Value`, `IsQuoted`, `Start` (offset in the raw text), `IsFlag`, `IsEndOfFlags` |
| `CommandArguments` | The result of `Split`: positional tokens, flags, and the position of the last flag |

`CommandTokenizer` handles quoting as follows:

- `"…"` and `'…'` group words into one token.
- It also accepts the **typographic quotes** that phone keyboards insert: `“…”`, `„…“`, `«…»`.
- Inside quotes, `\"` escapes the quote character.
- An apostrophe inside a word (`don't`) is literal, because a quote only counts at the start of a token.

`Split` handles flags as follows:

- A flag takes the next token as its value, unless it is a known switch, is written as `--name=value`, or is followed by another flag.
- A bare `--` ends flag parsing, so `-- --literal` is positional.

### Binding (`Binding/`)

| Type | Purpose |
|---|---|
| `IArgumentConverter` / `ArgumentConverter<T>` | Converts one raw string to a parameter type. `DisplayName` appears in error messages. |
| `IArgumentConverterProvider` | Finds the converter for a type (details below) |
| `ICommandArgumentBinder` / `CommandArgumentBinder` | Binds tokens to `CommandDescriptor.Parameters` |
| `ArgumentBindingResult` (union) | `union ArgumentBindingResult(BoundArguments, ArgumentBindingError)`: either `BoundArguments.Values` or `ArgumentBindingError.Message`. You cannot read values from a failed binding. |

Converter lookup order:

1. Registered converters. The last registration for a type wins. The built-ins are `string`, `bool` (accepting yes/no, on/off, 1/0) and `Recipient`.
2. `Nullable<T>`, which unwraps to `T`.
3. Enums (case-insensitive names; numbers are rejected).
4. Any type implementing `IParsable<T>`, parsed with the invariant culture. This covers `int`, `long`, `double`, `decimal`, `Guid`, `TimeSpan`, `DateTimeOffset`, `PhoneNumber`, `GroupId`, …

Binding rules:

- **Positional parameters** bind in declaration order.
- **Optional parameters:** a parameter is optional if it has a default value, is nullable (`int?`, `string?`), or is a switch.
- **Remainder:** a `[Remainder]` parameter gets the rest of the text verbatim, keeping multiple spaces. If flags follow it, the remaining token values are joined with single spaces instead.
- **Errors** are user-facing, and the result handler adds the usage line:
  - `Missing argument <b>.`
  - `'x' is not a valid whole number for <b>.`
  - `Too many arguments ('3' was not expected).`
  - `Option --times requires a value.`
  - `Unknown option --loud.`
- **Missing converter:** a parameter type without any converter is a programming error. It throws `InvalidOperationException`, which surfaces as a faulted command.

### Preconditions (`Preconditions/Preconditions.cs`)

| Type | Purpose |
|---|---|
| `PreconditionAttribute` | Base class. `CheckAsync(context, ct)` returns a `PreconditionResult`. `ErrorMessage` overrides the reply (an empty string fails silently). `Order` sorts the checks. |
| `RequireAdminAttribute` | The sender is in `Signal:Commands:Admins` (phone number or UUID) |
| `RequireGroupAttribute` / `RequireDirectMessageAttribute` | Restricts where the command can be used |
| `RequireGroupAdminAttribute` | The sender is an admin **of the Signal group** (looked up live via `IGroupService`) |
| `CooldownAttribute(seconds)` | Rate-limits one command, per `Scope` (`PerSender`, `PerConversation`, `Global`). `Order = 1000`, so it runs last and failed permission checks don't consume the cooldown. |
| `ICooldownTracker` | Stores cooldown expirations. The default is in memory and uses `TimeProvider`; replace it for multi-instance deployments. |

Preconditions on a module class apply to all of its commands. Preconditions run **before** argument
binding, so users without permission never see usage details.

### Metadata and registry

| Type | Purpose |
|---|---|
| `CommandParameter` | A bindable parameter: name, type, optional/default, remainder, flag name, summary. `ToString()` produces its usage fragment. |
| `CommandDescriptor` | Everything about one command. `FormatUsage(prefix)` generates `/add <a> <b>` when no custom `Usage` is set. |
| `CommandCatalog` | The registration-time list of command types, modules and descriptors. The builder fills it. |
| `ICommandRegistry` / `CommandRegistry` | Built **lazily on first use**, so every registration is complete. Rejects duplicate names or aliases with a clear error. Honours `CaseSensitive` and skips `HelpModule` when `EnableHelp` is false. |
| `CommandDescriptorFactory` (internal) | Reflection at startup only. Module methods are invoked through **compiled expression trees**, and modules are created with a cached `ActivatorUtilities` factory. |

### Execution and results

| Type | Purpose |
|---|---|
| `CommandContext` | What a command sees. See the table below. |
| `ICommandExecutor` / `CommandExecutor` | Lookup → preconditions → binding → invoke. Exceptions become `Faulted` results; only shutdown cancellation propagates. |
| `CommandResult` (union) | Exactly one outcome case (see below). Also exposes `Parsed` and `IsSuccess`. It is stored in `MessageContext.Items[typeof(CommandResult)]`. |
| `ICommandResultHandler` | Informs the user. The default handler replies with the unknown-command message (if enabled), the binding error plus usage, the precondition reason, or the generic `ErrorMessage`. Exception details are never sent to users. Replace it to localise messages or log elsewhere. |
| `CommandMiddleware` | The pipeline step tying everything together. Reactions are never treated as commands. Messages already marked `IsHandled` are skipped. |
| `HelpModule` | Built-in `help` / `?` / `commands`. It lists visible commands, or describes one with usage, aliases and parameter summaries. |

`CommandContext` exposes:

- the triggering message and account: `Message`, `Envelope`, `Data`, `Account`;
- who sent it and where: `Sender`, `Conversation`, `IsGroup`, `Group`;
- the invocation: `Parsed`, `Command`, `Arguments`, `Flags`;
- the scope: `Services`, `CancellationToken`;
- reply helpers: `ReplyAsync`, `ReplyStyledAsync`, `ReactAsync`.

### `CommandResult`: a C# 15 union

```csharp
public union CommandResult(CommandSucceeded, CommandNotFound, CommandPreconditionFailed, CommandBindingFailed, CommandFaulted);
```

| Case | Data | When |
|---|---|---|
| `CommandSucceeded` | `Parsed`, `Command` | The command ran to completion |
| `CommandNotFound` | `Parsed` | No command matches the typed name or alias |
| `CommandPreconditionFailed` | `Parsed`, `Command`, `Reason?` | A precondition rejected the call |
| `CommandBindingFailed` | `Parsed`, `Command`, `Error` | The arguments were invalid |
| `CommandFaulted` | `Parsed`, `Command`, `Exception` | The command threw |

Each case carries only the data that exists for it. `CommandNotFound` has no command, and only
`CommandFaulted` has an exception, so there are no nullable fields to check or `!` operators to add.
Switching over the result is exhaustive, so handlers that forget an outcome get a compiler warning:

```csharp
var reply = result switch
{
    CommandSucceeded => null,
    CommandNotFound notFound => $"Unknown command {notFound.Parsed.Name}",
    CommandPreconditionFailed failed => failed.Reason,
    CommandBindingFailed failed => $"{failed.Error}\nUsage: {failed.Command.FormatUsage(failed.Parsed.Prefix)}",
    CommandFaulted => "Something went wrong.",
    null => null,                                    // default(CommandResult)
};
```

## Tips

- **Long-running work:** avoid awaiting it inside a command, because it blocks its conversation partition. Enqueue the work instead, and reply when it is done.
- **Ignoring commands in event handlers:** check `MessageContext.Items[typeof(CommandResult)]` in later middleware, or compare `MessageReceived.Message.Text` against your prefixes.
