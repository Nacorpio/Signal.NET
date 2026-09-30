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

## Command groups

`[CommandGroup]` on a module (or `ICommand` class) puts its commands under a group, so they're invoked as
`/group command`:

```csharp
[CommandGroup("playlist", Aliases = ["pl"], Description = "Manage the playlist"), RequireGroup]
public sealed class PlaylistModule : CommandModule
{
    [Command("add")]    public Task AddAsync([Remainder] string song) => ReplyAsync($"Added {song}");
    [Command("remove")] public Task RemoveAsync(int index) => ReplyAsync($"Removed #{index}");
}
```

- **Names:** any combination of group and command aliases works (`/pl add …`). Usage lines, help and logs use the
  full name (`CommandDescriptor.FullName`, e.g. `playlist add`).
- **Shared preconditions:** preconditions on the module apply to every command in the group.
- **Separate state:** cooldowns are keyed by the full name, so `/playlist add` and `/queue add` don't share one.
- **Typos** get a suggestion: `/hlep` → "… Did you mean /help?", and `/playlist remvoe` → "… Did you mean
  /playlist remove?". Hidden commands are never suggested (`Commands:SuggestSimilarCommands`).
- **Missing or unknown subcommands** get a reply listing the group's commands (`/playlist needs a subcommand: add,
  remove.`) instead of "Unknown command". `/help playlist` describes the group, `/help playlist add` the command.
- **Conflicts** are rejected at startup: a group can't have the same name or alias as a top-level command.
  Several modules may share a group name; their commands are merged.

## Per-conversation settings

A group (or direct chat) can have its own prefixes and disabled commands, stored in `IConversationSettingsStore`:

```csharp
[Command("prefix"), RequireGroup, RequireRole(Role.GroupAdmin)]
public async Task PrefixAsync(string prefix)
{
    var store = Context.Services.GetRequiredService<IConversationSettingsStore>();
    var current = await Context.Message.GetConversationSettingsAsync() ?? new ConversationSettings();
    await store.SetAsync(Context.Account, Context.Conversation, current with { Prefixes = [prefix] });
    await ReplyAsync($"This group now uses {prefix} as prefix.");
}
```

- **`Prefixes`** replace `Commands:Prefixes` in that conversation only.
- **`DisabledCommands`** lists full names (`ban`, `playlist add`) or group names (`playlist`), case-insensitive.
  Using one replies with `Commands:DisabledCommandMessage` (empty for no reply), and help and "Did you mean"
  treat them as hidden there.
- **`Culture`** is stored for localised replies.
- **Storage:** settings are loaded **once per text message** and cached in `MessageContext.Items`
  (`GetConversationSettingsAsync()`). The default store is in memory; implement `IConversationSettingsStore` to persist them.
- **Known limitation:** prompts recognise commands by the global prefixes only. In a conversation with its own
  prefix, an answer like `!help` is taken as the answer.

## Components

### Attributes (`Attributes.cs`)

| Attribute | Target | Purpose |
|---|---|---|
| `[Command(name)]` | method / class | Declares a command. Properties: `Aliases`, `Description`, `Usage`, `Hidden`. |
| `[Remainder]` | `string` parameter | Takes the rest of the text verbatim. Must be the last positional parameter. |
| `[Flag(name?)]` | parameter | Binds from `--name value` or `--name=value`. A `bool` flag is a switch (`--name`). |
| `[Summary(text)]` | parameter | Parameter description shown by `/help <command>` |
| `[Example(text)]` | method / command class | An example without prefix (`add 2 3`) shown by `/help <command>`. Repeatable. |
| `[Category(name)]` | module / method / command class | Heading for the command in `/help`. On a method it overrides the module's category. |
| `[CommandGroup(name)]` | module / command class | Puts the commands under `/name`. Properties: `Aliases`, `Description`. See [Command groups](#command-groups). |

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
- **Collections:** a `params T[]`, `T[]`, `List<T>` or `IEnumerable<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>`/
  `IList<T>`/`ICollection<T>` parameter takes **all remaining positional arguments**, each converted to `T` (so
  `@mentions` resolve per element). Like `[Remainder]` it must be the last positional parameter, and it can't be a
  `[Flag]`. `params` may be empty. Other collections are required unless they have a default or are nullable.
  Usage shows `<people...>` or `[numbers...]`.
- **Remainder:** a `[Remainder]` parameter gets the rest of the text verbatim, keeping multiple spaces. If flags follow it, the remaining token values are joined with single spaces instead.
- **@mentions:** Signal replaces a mention in the text with a placeholder character (U+FFFC) and lists the user in
  `DataMessage.Mentions`. A positional argument that is a mention binds as the mentioned user's phone number, or
  their UUID if the number is hidden. So `/kick @Bob` works with a `Recipient`, `PhoneNumber` or `AccountId`
  parameter. A `PhoneNumber` parameter rejects users with hidden numbers; use `Recipient` to accept both.
  Mentions in `[Remainder]` text and in `--option` values are left as the placeholder.
- **Errors** are user-facing, and the result handler adds the usage line:
  - `Missing argument <b>.`
  - `'x' is not a valid whole number for <b>.`
  - `Too many arguments ('3' was not expected).`
  - `Option --times requires a value.`
  - `Unknown option --loud.`
  - `Could not resolve the @mention for <user>.` (a placeholder without a matching mention)
- **Missing converter:** a parameter type without any converter is a programming error. It throws `InvalidOperationException`, which surfaces as a faulted command.

### Preconditions (`Preconditions/Preconditions.cs`)

| Type | Purpose |
|---|---|
| `PreconditionAttribute` | Base class. `CheckAsync(context, ct)` returns a `PreconditionResult`. `ErrorMessage` overrides the reply (an empty string fails silently). `Order` sorts the checks. |
| `RequireAdminAttribute` | The sender is in `Signal:Commands:Admins` (phone number or UUID) |
| `RequireGroupAttribute` / `RequireDirectMessageAttribute` | Restricts where the command can be used |
| `RequireGroupAdminAttribute` | The sender is an admin **of the Signal group** (looked up live via `IGroupService`) |
| `RequireRoleAttribute(roles…)` | The sender has **at least one** of the roles, per the registered `IRoleProvider`s. Built in: `Commands:Roles` (role → numbers/UUIDs), `Role.Admin` (also `Commands:Admins`), `Role.GroupAdmin` (Signal group admins). `AddRoleProvider<T>()` adds your own, e.g. database-backed. |
| `CooldownAttribute(seconds)` | Rate-limits one command, per `Scope` (`PerSender`, `PerConversation`, `Global`). `Order = 1000`, so it runs last and failed permission checks don't consume the cooldown. |
| `ICooldownTracker` | Stores cooldown expirations. The default is in memory and uses `TimeProvider`; replace it for multi-instance deployments. |

Preconditions on a module class apply to all of its commands. Preconditions run **before** argument
binding, so users without permission never see usage details.

### Metadata and registry

| Type | Purpose |
|---|---|
| `CommandParameter` | A bindable parameter: name, type, optional/default, remainder, flag name, summary, and `ElementType`/`IsCollection` for collections. `ToString()` produces its usage fragment. |
| `CommandDescriptor` | Everything about one command. `FormatUsage(prefix)` generates `/add <a> <b>` when no custom `Usage` is set. `Group` (`CommandGroupInfo`) and `FullName` describe grouped commands. |
| `CommandCatalog` | The registration-time list of command types, modules and descriptors. The builder fills it. |
| `ICommandRegistry` / `CommandRegistry` | Built **lazily on first use**, so every registration is complete. Rejects duplicate names or aliases with a clear error. Honours `CaseSensitive` and skips `HelpModule` when `EnableHelp` is false. Grouped commands are keyed as `group command`; `GetGroup(name)` lists a group. |
| `CommandDescriptorFactory` (internal) | Reflection at startup only. Module methods are invoked through **compiled expression trees**, and modules are created with a cached `ActivatorUtilities` factory. |

### Execution and results

| Type | Purpose |
|---|---|
| `CommandContext` | What a command sees. See the table below. |
| `ICommandExecutor` / `CommandExecutor` | Lookup → preconditions → binding → invoke. Exceptions become `Faulted` results; only shutdown cancellation propagates. |
| `CommandResult` (union) | Exactly one outcome case (see below). Also exposes `Parsed` and `IsSuccess`. It is stored in `MessageContext.Items[typeof(CommandResult)]`. |
| `ICommandResultHandler` | Informs the user. The default handler replies with the unknown-command message (if enabled), the binding error plus usage, the precondition reason, or the generic `ErrorMessage`. Exception details are never sent to users. Replace it to localise messages or log elsewhere. |
| `CommandMiddleware` | The pipeline step tying everything together. Reactions are never treated as commands. Messages already marked `IsHandled` are skipped. |
| `HelpModule` | Built-in `help` / `?` / `commands`. `/help` lists visible commands under headings ("General", each command group, each `[Category]`) when there's more than one section, and a flat list otherwise. It pages by `HelpPageSize` (`/help 2`). `/help <command>` shows usage, aliases, parameter summaries and examples; `/help <group>` lists a group. |

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

- **Long-running work:** don't await it inside a command, because it blocks its conversation partition. Use
  `RunInBackgroundAsync` instead; it returns as soon as the work is queued:

  ```csharp
  [Command("report")]
  public async Task ReportAsync()
  {
      await ReplyAsync("Working on it...");
      await RunInBackgroundAsync(async work =>
      {
          var reports = work.Services.GetRequiredService<IReportService>();   // resolve from the work's own scope
          await work.ReplyAsync(await reports.BuildAsync(work.CancellationToken));
      });
  }
  ```

  The work runs in **its own DI scope**, because the message's scope is disposed as soon as the command returns.
  Don't capture the module's scoped services in the lambda. `work.ReplyAsync` answers in the original
  conversation. Outside commands, use `messageContext.QueueBackgroundWorkAsync(...)` or `IBackgroundWorkQueue`.
- **Asking follow-up questions:** background work can wait for the sender's next message:

  ```csharp
  [Command("order")]
  public Task OrderAsync() => RunInBackgroundAsync(async work =>
  {
      var count = await work.PromptAsync<int>("How many?");         // any IParsable<T>; asks again on invalid input
      if (!count.IsAnswered) { await work.ReplyAsync("Never mind."); return; }
      var name = await work.PromptAsync("Name for the order?");
      await work.ReplyAsync($"Ordered {count.Value} for {name.Value}.");
  }).AsTask();
  ```

  - Only the sender who triggered the work can answer; in groups, other members' messages are ignored.
  - Messages that parse as a command are never taken as answers, so `/help` still works while a prompt waits.
  - The answer is consumed: it raises no events and runs no commands.
  - `PromptResult.Status` is `Answered`, `TimedOut` (after `Background:PromptTimeout`, default 2 minutes, or when
    a newer prompt to the same sender replaces it) or `Invalid` (after `attempts` unparsable answers).
  - Prompts live in background work, not in commands, on purpose: a command waiting for an answer would hold
    its conversation partition, and every conversation sharing it, until the answer or the timeout.
- **Reminders and digests:** `ScheduleReplyAsync` sends a message into the conversation later, once or repeatedly:

  ```csharp
  [Command("remind")]
  public async Task RemindAsync(TimeSpan delay, [Remainder] string text)
  {
      var reminder = await ScheduleReplyAsync(text, DateTimeOffset.UtcNow + delay);
      await ReplyAsync($"OK, reminder {reminder.Id} set.");
  }
  ```

  Use `IMessageScheduler` to schedule to any recipient, list (`ListAsync`) or cancel (`CancelAsync`). A
  `repeatEvery` of at least one minute makes it recurring. Scheduled messages are **kept in memory** by default
  and lost on restart; register your own `IScheduledMessageStore` (e.g. backed by a database) before `AddSignal`
  to keep them.
- **Ignoring commands in event handlers:** check `MessageContext.Items[typeof(CommandResult)]` in later middleware, or compare `MessageReceived.Message.Text` against your prefixes.
