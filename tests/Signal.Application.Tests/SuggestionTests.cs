using Signal.Application.Commands;

namespace Signal.Application.Tests;

public class SuggestionTests
{
    [Theory]
    [InlineData("help", "help", 0)]
    [InlineData("hlep", "help", 1)]
    [InlineData("HELP", "help", 0)]
    [InlineData("ad", "add", 1)]
    [InlineData("remvoe", "remove", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    public void Distance_counts_edits_and_adjacent_swaps(string a, string b, int expected) =>
        Assert.Equal(expected, CommandSuggestions.Distance(a, b));

    [Theory]
    [InlineData("hlep", "help")]
    [InlineData("ad", "add")]
    [InlineData("colour", "color")]
    [InlineData("gret", "greet")]
    public void Suggests_close_names(string typed, string expected) =>
        Assert.Equal(expected, CommandSuggestions.Closest(typed, ["add", "help", "greet", "color", "?"]));

    [Theory]
    [InlineData("x")]        // one-letter input must not match "?" or other one-letter names
    [InlineData("zzz")]
    [InlineData("delete")]
    public void Suggests_nothing_when_no_name_is_close(string typed) =>
        Assert.Null(CommandSuggestions.Closest(typed, ["add", "help", "greet", "?"]));

    private static TestHarness Harness(bool suggest = true) => TestHarness.Create(
        o => o.Commands.SuggestSimilarCommands = suggest,
        (_, catalog) =>
        {
            catalog.AddModule(typeof(TestModule));
            catalog.AddModule(typeof(PlaylistModule));
        });

    [Theory]
    [InlineData("/ad 1 2", "Unknown command 'ad'. Send /help for a list of commands. Did you mean /add?")]
    [InlineData("/playlst add x", "Unknown command 'playlst'. Send /help for a list of commands. Did you mean /playlist?")]
    [InlineData("/playlist remvoe 1", "Unknown subcommand 'remvoe' for /playlist. Available: add, remove. Did you mean /playlist remove?")]
    [InlineData("/nothingclose", "Unknown command 'nothingclose'. Send /help for a list of commands.")]
    public async Task Unknown_commands_get_a_suggestion(string text, string expected)
    {
        await using var harness = Harness();

        await harness.ReceiveAsync(text, inGroup: true);

        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Hidden_commands_are_never_suggested()
    {
        await using var harness = Harness();

        // "purge" is a hidden playlist command.
        await harness.ReceiveAsync("/playlist purg", inGroup: true);

        Assert.DoesNotContain("purge", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Suggestions_can_be_disabled()
    {
        await using var harness = Harness(suggest: false);

        await harness.ReceiveAsync("/ad 1 2");

        Assert.Equal("Unknown command 'ad'. Send /help for a list of commands.", harness.Signal.LastReply);
    }
}
