using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Commands;
using Signal.Application.Commands.Preconditions;

namespace Signal.Application.Tests;

[CommandGroup("playlist", Aliases = ["pl"], Description = "Manage the playlist"), RequireGroup]
public sealed class PlaylistModule : CommandModule
{
    [Command("add", Aliases = ["+"], Description = "Adds a song"), Cooldown(60)]
    public Task AddAsync([Remainder] string song) => ReplyAsync($"added {song}");

    [Command("remove")]
    public Task RemoveAsync(int index) => ReplyAsync($"removed {index}");

    [Command("purge", Hidden = true)]
    public Task PurgeAsync() => ReplyAsync("purged");
}

[CommandGroup("queue")]
public sealed class QueueModule : CommandModule
{
    [Command("add"), Cooldown(60)]
    public Task AddAsync(string item) => ReplyAsync($"queued {item}");
}

/// <summary>Invalid: the group name clashes with the top-level "add" command of <see cref="TestModule"/>.</summary>
[CommandGroup("add")]
public sealed class ClashingModule : CommandModule
{
    [Command("x")]
    public Task XAsync() => Task.CompletedTask;
}

public class CommandGroupTests
{
    private static TestHarness Harness() => TestHarness.Create(setup: (_, catalog) =>
    {
        catalog.AddModule(typeof(TestModule));
        catalog.AddModule(typeof(PlaylistModule));
        catalog.AddModule(typeof(QueueModule));
    });

    [Theory]
    [InlineData("/playlist add Bohemian  Rhapsody", "added Bohemian  Rhapsody")]
    [InlineData("/pl add x", "added x")]
    [InlineData("/PL + y", "added y")]
    [InlineData("/playlist   remove 2", "removed 2")]
    public async Task Resolves_group_and_command_names_and_aliases(string text, string expected)
    {
        await using var harness = Harness();

        await harness.ReceiveAsync(text, inGroup: true);

        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Group_preconditions_apply_to_every_command()
    {
        await using var harness = Harness();

        var context = await harness.ReceiveAsync("/playlist remove 1");

        Assert.IsType<CommandPreconditionFailed>(context.Items[typeof(CommandResult)] is CommandResult r ? r.Value : null);
    }

    [Fact]
    public async Task Cooldowns_are_per_full_command_name()
    {
        await using var harness = Harness();

        await harness.ReceiveAsync("/playlist add a", inGroup: true);
        await harness.ReceiveAsync("/playlist add b", inGroup: true);
        var playlistBlocked = harness.Signal.LastReply;
        await harness.ReceiveAsync("/queue add c", inGroup: true);

        Assert.DoesNotContain("added b", playlistBlocked);
        Assert.Equal("queued c", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Binding_errors_show_the_full_usage()
    {
        await using var harness = Harness();

        await harness.ReceiveAsync("/playlist remove x", inGroup: true);

        Assert.Equal("'x' is not a valid whole number for <index>.\nUsage: /playlist remove <index>", harness.Signal.LastReply);
    }

    [Theory]
    [InlineData("/playlist", "/playlist needs a subcommand: add, remove. Send /help playlist for details.")]
    [InlineData("/pl shuffle", "Unknown subcommand 'shuffle' for /pl. Available: add, remove.")]
    public async Task Missing_or_unknown_subcommands_list_the_group(string text, string expected)
    {
        await using var harness = Harness();

        await harness.ReceiveAsync(text, inGroup: true);

        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Help_lists_full_names_and_describes_groups_and_subcommands()
    {
        await using var harness = Harness();

        await harness.ReceiveAsync("/help");
        var all = harness.Signal.LastReply!;
        await harness.ReceiveAsync("/help playlist");
        var group = harness.Signal.LastReply!;
        await harness.ReceiveAsync("/help /pl  add");
        var sub = harness.Signal.LastReply!;

        Assert.Contains("/playlist add – Adds a song", all);
        Assert.Contains("/queue add", all);
        Assert.DoesNotContain("purge", all);
        Assert.StartsWith("/playlist <command>\nManage the playlist\nAliases: /pl\n", group.ReplaceLineEndings("\n"));
        Assert.Contains("/playlist remove", group);
        Assert.StartsWith("/playlist add <song...>\nAdds a song\nAliases: /playlist +", sub.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task A_group_named_like_a_command_is_rejected()
    {
        await using var harness = TestHarness.Create(setup: (_, catalog) =>
        {
            catalog.AddModule(typeof(TestModule));
            catalog.AddModule(typeof(ClashingModule));
        });

        var error = Assert.Throws<InvalidOperationException>(() => harness.Services.GetRequiredService<ICommandRegistry>().Commands);
        Assert.Contains("'add' is used both as a command name or alias and as a command group", error.Message);
    }
}
