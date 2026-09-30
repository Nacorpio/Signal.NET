using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Commands;
using Signal.Application.Configuration;

namespace Signal.Application.Tests;

[Category("Moderation")]
public sealed class ModerationModule : CommandModule
{
    [Command("kick", Description = "Removes a member"), Example("kick +15550002222")]
    public Task KickAsync(string member) => Task.CompletedTask;

    [Command("warn")]
    public Task WarnAsync(string member) => Task.CompletedTask;

    [Command("stats"), Category("Info")]
    public Task StatsAsync() => Task.CompletedTask;
}

public sealed class ManyModule : CommandModule
{
    [Command("c01")] public void C01() { }
    [Command("c02")] public void C02() { }
    [Command("c03")] public void C03() { }
    [Command("c04")] public void C04() { }
    [Command("c05")] public void C05() { }
}

public class HelpTests
{
    private static TestHarness Harness(int pageSize = 20, params Type[] modules) => TestHarness.Create(
        o => o.Commands.HelpPageSize = pageSize,
        (_, catalog) =>
        {
            foreach (var module in modules)
            {
                catalog.AddModule(module);
            }
        });

    [Fact]
    public async Task Sections_appear_for_groups_and_categories()
    {
        await using var harness = Harness(20, typeof(ModerationModule), typeof(PlaylistModule));

        await harness.ReceiveAsync("/help");

        Assert.Equal(
            """
            Available commands:

            General
            /help – Lists all commands or shows details of one command.

            /playlist – Manage the playlist
            /playlist add – Adds a song
            /playlist remove

            Info
            /stats

            Moderation
            /kick – Removes a member
            /warn
            Send /help <command> for details.
            """,
            harness.Signal.LastReply!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task A_single_section_stays_a_flat_list()
    {
        await using var harness = Harness(20, typeof(ManyModule));

        await harness.ReceiveAsync("/help");

        Assert.DoesNotContain("General", harness.Signal.LastReply);
        Assert.StartsWith("Available commands:\n/c01\n", harness.Signal.LastReply!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Long_lists_are_paged()
    {
        // Five module commands plus /help = 6 entries at 4 per page.
        await using var harness = Harness(4, typeof(ManyModule));

        await harness.ReceiveAsync("/help");
        var first = harness.Signal.LastReply!.ReplaceLineEndings("\n");
        await harness.ReceiveAsync("/help 2");
        var second = harness.Signal.LastReply!.ReplaceLineEndings("\n");
        await harness.ReceiveAsync("/help 3");

        Assert.StartsWith("Available commands (page 1/2):\n/c01\n/c02\n/c03\n/c04\nSend /help 2 for more.\n", first);
        Assert.StartsWith("Available commands (page 2/2):\n/c05\n/help", second);
        Assert.DoesNotContain("for more", second);
        Assert.Equal("There are only 2 pages of commands.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Paging_can_be_disabled()
    {
        await using var harness = Harness(0, typeof(ManyModule));

        await harness.ReceiveAsync("/help 2");

        Assert.Equal("There is only 1 page of commands.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Command_details_include_examples()
    {
        await using var harness = Harness(20, typeof(ModerationModule));

        await harness.ReceiveAsync("/help kick");

        Assert.Equal("/kick <member>\nRemoves a member\nExamples:\n  /kick +15550002222", harness.Signal.LastReply!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Negative_page_size_fails_validation()
    {
        await using var harness = Harness(-1);

        var error = Assert.Throws<OptionsValidationException>(() => harness.Services.GetRequiredService<IOptions<SignalOptions>>().Value);
        Assert.Contains("HelpPageSize", error.Message);
    }
}
