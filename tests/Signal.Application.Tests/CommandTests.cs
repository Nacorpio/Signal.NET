using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Commands;
using Signal.Application.Commands.Preconditions;
using Signal.Application.Events;
using Signal.Domain.Events;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

public sealed class TestModule : CommandModule
{
    [Command("add", Aliases = ["plus"], Description = "Adds two numbers")]
    public Task AddAsync(int a, int b) => ReplyAsync((a + b).ToString(CultureInfo.InvariantCulture));

    [Command("echo")]
    public Task EchoAsync([Remainder] string text, [Flag] bool upper) => ReplyAsync(upper ? text.ToUpperInvariant() : text);

    [Command("greet")]
    public Task GreetAsync(string? name = null, [Flag("times")] int times = 1) =>
        ReplyAsync(string.Join(" ", Enumerable.Repeat($"hi {name ?? "you"}", times)));

    [Command("color")]
    public Task ColorAsync(ConsoleColor color) => ReplyAsync(color.ToString());

    [Command("call")]
    public Task CallAsync(PhoneNumber number, TimeSpan? after = null) => ReplyAsync($"{number} {after}".Trim());

    [Command("sync")]
    public void Sync() => Context.Message.Items["sync"] = true;

    [Command("value")]
    public ValueTask<int> ValueAsync() => ValueTask.FromResult(1);

    [Command("secret"), RequireAdmin]
    public Task SecretAsync() => ReplyAsync("secret");

    [Command("grouponly"), RequireGroup]
    public Task GroupOnlyAsync() => ReplyAsync("group");

    [Command("groupadmin"), RequireGroupAdmin]
    public Task GroupAdminAsync() => ReplyAsync("group admin");

    [Command("cool"), Cooldown(60)]
    public Task CoolAsync() => ReplyAsync("cool");

    [Command("boom")]
    public Task BoomAsync() => throw new InvalidOperationException("kaboom");
}

[Command("classic", Aliases = ["c"], Description = "Class-based command")]
public sealed class ClassicCommand : CommandBase
{
    public override Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken) =>
        context.ReplyAsync($"args={string.Join(",", context.Arguments)} n={context.Flags.GetValueOrDefault("n")}", cancellationToken: cancellationToken);
}

public sealed class RecordingHandler : IEventHandler<MessageReceived>
{
    public static List<string?> Received { get; } = [];

    public Task HandleAsync(MessageReceived domainEvent, CancellationToken cancellationToken)
    {
        lock (Received)
        {
            Received.Add(domainEvent.Message.Text);
        }

        return Task.CompletedTask;
    }
}

public class CommandTests
{
    private static TestHarness Harness(Action<Configuration.SignalOptions>? configure = null) => TestHarness.Create(configure, (services, catalog) =>
    {
        catalog.AddModule(typeof(TestModule));
        catalog.AddCommand(typeof(ClassicCommand));
        services.AddScoped<ClassicCommand>();
    });

    [Theory]
    [InlineData("/add 2 3", "5")]
    [InlineData("/PLUS 2 3", "5")]
    [InlineData("/echo  hello   world ", "hello   world")]
    [InlineData("/echo \"quoted text\"", "quoted text")]
    [InlineData("/echo --upper shout it", "SHOUT IT")]
    [InlineData("/echo shout it --upper", "SHOUT IT")]
    [InlineData("/greet", "hi you")]
    [InlineData("/greet bob --times 2", "hi bob hi bob")]
    [InlineData("/greet --times=2 bob", "hi bob hi bob")]
    [InlineData("/color darkred", "DarkRed")]
    [InlineData("/call +1-555-000-2222 00:05:00", "+15550002222 00:05:00")]
    [InlineData("/classic a \"b c\" --n 5", "args=a,b c n=5")]
    [InlineData("/c", "args= n=")]
    public async Task Binds_arguments_and_replies(string text, string expected)
    {
        await using var harness = Harness();
        var context = await harness.ReceiveAsync(text);

        Assert.True(context.IsHandled);
        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Theory]
    [InlineData("/add 2", "Missing argument <b>.\nUsage: /add <a> <b>")]
    [InlineData("/add 2 x", "'x' is not a valid whole number for <b>.\nUsage: /add <a> <b>")]
    [InlineData("/add 1 2 3", "Too many arguments ('3' was not expected).\nUsage: /add <a> <b>")]
    [InlineData("/greet --times", "Option --times requires a value.\nUsage: /greet [name] [--times <times>]")]
    [InlineData("/greet --loud", "Unknown option --loud.\nUsage: /greet [name] [--times <times>]")]
    [InlineData("/color purple", "'purple' is not a valid one of: black, darkblue, darkgreen, darkcyan, darkred, darkmagenta, darkyellow, gray, darkgray, blue, green, cyan, red, magenta, yellow, white for <color>.\nUsage: /color <color>")]
    public async Task Reports_binding_errors_with_usage(string text, string expected)
    {
        await using var harness = Harness();
        await harness.ReceiveAsync(text);

        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Supports_void_and_ValueTask_methods()
    {
        await using var harness = Harness();
        var context = await harness.ReceiveAsync("/sync");
        await harness.ReceiveAsync("/value");

        Assert.Equal(true, context.Items["sync"]);
        Assert.IsType<CommandSucceeded>(((CommandResult)context.Items[typeof(CommandResult)]!).Value);
    }

    [Theory]
    [InlineData("/add 1 2", typeof(CommandSucceeded))]
    [InlineData("/nope", typeof(CommandNotFound))]
    [InlineData("/add x", typeof(CommandBindingFailed))]
    [InlineData("/grouponly", typeof(CommandPreconditionFailed))]
    [InlineData("/boom", typeof(CommandFaulted))]
    public async Task Result_union_holds_the_matching_case(string text, Type expected)
    {
        await using var harness = Harness();
        var context = await harness.ReceiveAsync(text);
        var result = (CommandResult)context.Items[typeof(CommandResult)]!;

        Assert.IsType(expected, result.Value);
        Assert.Equal(expected == typeof(CommandSucceeded), result.IsSuccess);

        // The union is exhaustive: every outcome must be handled, and each case carries only its own data.
        var summary = result switch
        {
            CommandSucceeded s => $"ok {s.Command.Name}",
            CommandNotFound n => $"unknown {n.Parsed.Name}",
            CommandBindingFailed b => $"binding {b.Command.Name}",
            CommandPreconditionFailed p => $"precondition {p.Command.Name}",
            CommandFaulted f => $"faulted {f.Exception.GetType().Name}",
            null => "none",
        };
        Assert.NotEqual("none", summary);
    }

    [Fact]
    public async Task Replies_to_unknown_commands()
    {
        await using var harness = Harness();
        await harness.ReceiveAsync("/nope");

        Assert.Equal("Unknown command 'nope'. Send /help for a list of commands.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Can_ignore_unknown_commands()
    {
        await using var harness = Harness(o => o.Commands.RespondToUnknown = false);
        await harness.ReceiveAsync("/nope");

        Assert.Empty(harness.Signal.Sent);
    }

    [Fact]
    public async Task Ignores_plain_text()
    {
        await using var harness = Harness();
        var context = await harness.ReceiveAsync("just chatting");

        Assert.False(context.IsHandled);
        Assert.Empty(harness.Signal.Sent);
    }

    [Fact]
    public async Task Edits_deletes_and_stickers_never_run_commands()
    {
        await using var harness = Harness();
        var account = PhoneNumber.Parse(TestHarness.Account);
        var alice = new Sender(PhoneNumber.Parse(TestHarness.Alice), null, "Alice");

        // Editing a message into a command must not execute it.
        var edit = await harness.ReceiveAsync(new IncomingEnvelope(account, alice, 2,
            new EditMessage(1, new DataMessage(2, "/add 1 2"))));
        var delete = await harness.ReceiveAsync(new IncomingEnvelope(account, alice, 3,
            new DataMessage(3, null) { RemoteDelete = new RemoteDelete(1) }));
        var sticker = await harness.ReceiveAsync(new IncomingEnvelope(account, alice, 4,
            new DataMessage(4, null) { Sticker = new Sticker("abc", 1) }));

        Assert.False(edit.IsHandled);
        Assert.False(delete.IsHandled);
        Assert.False(sticker.IsHandled);
        Assert.Empty(harness.Signal.Sent);
    }

    [Fact]
    public async Task Honors_case_sensitivity()
    {
        await using var harness = Harness(o => o.Commands.CaseSensitive = true);
        await harness.ReceiveAsync("/ADD 1 2");

        Assert.StartsWith("Unknown command", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Help_lists_commands_and_describes_one()
    {
        await using var harness = Harness();
        await harness.ReceiveAsync("/help");
        var list = harness.Signal.LastReply!;
        await harness.ReceiveAsync("/help add");

        Assert.Contains("/add – Adds two numbers", list);
        Assert.Contains("/classic – Class-based command", list);
        Assert.Equal("/add <a> <b>\nAdds two numbers\nAliases: /plus", harness.Signal.LastReply!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task RequireAdmin_checks_configured_admins()
    {
        await using var harness = Harness(o => o.Commands.Admins = [TestHarness.Alice]);
        await harness.ReceiveAsync("/secret");
        Assert.Equal("secret", harness.Signal.LastReply);

        await harness.ReceiveAsync("/secret", from: "+15550009999");
        Assert.Equal("This command is restricted to administrators.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task RequireGroup_and_RequireGroupAdmin()
    {
        await using var harness = Harness();
        await harness.ReceiveAsync("/grouponly");
        Assert.Equal("This command can only be used in groups.", harness.Signal.LastReply);

        await harness.ReceiveAsync("/grouponly", inGroup: true);
        Assert.Equal("group", harness.Signal.LastReply);

        await harness.ReceiveAsync("/groupadmin", inGroup: true);
        Assert.Equal("Only group admins can use this command.", harness.Signal.LastReply);

        harness.Signal.GroupAdmins.Add(TestHarness.Alice);
        await harness.ReceiveAsync("/groupadmin", inGroup: true);
        Assert.Equal("group admin", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Cooldown_blocks_repeated_use_per_sender()
    {
        await using var harness = Harness();
        await harness.ReceiveAsync("/cool");
        Assert.Equal("cool", harness.Signal.LastReply);

        await harness.ReceiveAsync("/cool");
        Assert.StartsWith("Please wait", harness.Signal.LastReply);

        await harness.ReceiveAsync("/cool", from: "+15550009999");
        Assert.Equal("cool", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Faulted_commands_reply_with_error_message()
    {
        await using var harness = Harness();
        var context = await harness.ReceiveAsync("/boom");

        var result = (CommandResult)context.Items[typeof(CommandResult)]!;
        var faulted = Assert.IsType<CommandFaulted>(result.Value);
        Assert.IsType<InvalidOperationException>(faulted.Exception);
        Assert.False(result.IsSuccess);
        Assert.Equal("boom", result.Parsed.Name);
        Assert.Equal("Sorry, something went wrong while executing this command.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Quotes_replies_when_configured()
    {
        await using var harness = Harness(o => o.Commands.QuoteReplies = true);
        await harness.ReceiveAsync("/add 1 1");

        Assert.NotNull(harness.Signal.Sent.Single().Quote);
    }

    [Fact]
    public async Task Replies_into_the_group_for_group_messages()
    {
        await using var harness = Harness();
        await harness.ReceiveAsync("/add 1 1", inGroup: true);

        Assert.Equal<Recipient>(TestHarness.Group, Assert.Single(harness.Signal.Sent.Single().Recipients));
    }

    [Fact]
    public async Task Duplicate_command_names_are_rejected()
    {
        await using var harness = TestHarness.Create(setup: (_, catalog) =>
        {
            catalog.Add(new CommandDescriptor("dup", (_, _) => Task.CompletedTask, typeof(object)));
            catalog.Add(new CommandDescriptor("other", (_, _) => Task.CompletedTask, typeof(object), aliases: ["dup"]));
        });

        var registry = harness.Services.GetRequiredService<ICommandRegistry>();
        var error = Assert.Throws<InvalidOperationException>(() => registry.Commands);
        Assert.Contains("'dup'", error.Message);
    }

    [Fact]
    public async Task Delegate_commands_are_supported()
    {
        await using var harness = TestHarness.Create(setup: (_, catalog) =>
            catalog.Add(new CommandDescriptor("hello", (ctx, _) => ctx.ReplyAsync("world"), typeof(object))));
        await harness.ReceiveAsync("/hello");

        Assert.Equal("world", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Help_can_be_disabled()
    {
        await using var harness = Harness(o => o.Commands.EnableHelp = false);
        await harness.ReceiveAsync("/help");

        Assert.StartsWith("Unknown command", harness.Signal.LastReply);
    }
}

public class PipelineTests
{
    [Fact]
    public async Task Dispatches_domain_events_to_handlers()
    {
        await using var harness = TestHarness.Create(setup: (services, _) =>
            services.AddScoped<IEventHandler<MessageReceived>, RecordingHandler>());
        var marker = Guid.NewGuid().ToString();
        await harness.ReceiveAsync(marker);

        lock (RecordingHandler.Received)
        {
            Assert.Contains(marker, RecordingHandler.Received);
        }
    }

    [Fact]
    public async Task Blocked_senders_are_ignored()
    {
        await using var harness = TestHarness.Create(o => o.AccessControl.BlockedSenders = [TestHarness.Alice]);
        await harness.ReceiveAsync("/help");

        Assert.Empty(harness.Signal.Sent);
    }

    [Fact]
    public async Task Allow_list_restricts_senders()
    {
        await using var harness = TestHarness.Create(o => o.AccessControl.AllowedSenders = ["+15550009999"]);
        await harness.ReceiveAsync("/help");
        Assert.Empty(harness.Signal.Sent);

        await harness.ReceiveAsync("/help", from: "+15550009999");
        Assert.Single(harness.Signal.Sent);
    }

    [Fact]
    public async Task Own_messages_are_ignored()
    {
        await using var harness = TestHarness.Create();
        await harness.ReceiveAsync("/help", from: TestHarness.Account);

        Assert.Empty(harness.Signal.Sent);
    }

    [Fact]
    public async Task Rate_limit_drops_excess_messages()
    {
        await using var harness = TestHarness.Create(o => o.RateLimit.PermitsPerWindow = 2);
        for (var i = 0; i < 5; i++)
        {
            await harness.ReceiveAsync("/help");
        }

        Assert.Equal(2, harness.Signal.Sent.Count);
    }
}
