using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Commands;
using Signal.Application.Pipeline;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

public sealed class CollectionModule : CommandModule
{
    [Command("sum")]
    public Task SumAsync(params int[] numbers) => ReplyAsync($"{numbers.Sum()} ({numbers.Length})");

    [Command("invite")]
    public Task InviteAsync(string group, List<PhoneNumber> people, [Flag] bool dry) =>
        ReplyAsync($"{group}: {string.Join(",", people.Select(p => p.Value))}{(dry ? " (dry)" : string.Empty)}");

    [Command("tags")]
    public Task TagsAsync(IReadOnlyList<string>? tags = null) => ReplyAsync(tags is null ? "none" : string.Join("|", tags));
}

public sealed class CollectionNotLastModule : CommandModule
{
    [Command("bad")]
    public Task BadAsync(int[] numbers, string name) => Task.CompletedTask;
}

public sealed class CollectionFlagModule : CommandModule
{
    [Command("bad")]
    public Task BadAsync([Flag] string[] names) => Task.CompletedTask;
}

public class CollectionParameterTests
{
    private static TestHarness Harness() => TestHarness.Create(setup: (_, catalog) => catalog.AddModule(typeof(CollectionModule)));

    [Theory]
    [InlineData("/sum 1 2 3", "6 (3)")]
    [InlineData("/sum", "0 (0)")]
    [InlineData("/sum 1 x 3", "'x' is not a valid whole number for <numbers>.\nUsage: /sum [numbers...]")]
    [InlineData("/invite club +15550002222 +15550003333 --dry", "club: +15550002222,+15550003333 (dry)")]
    [InlineData("/invite club", "Missing argument <people>.\nUsage: /invite <group> <people...> [--dry]")]
    [InlineData("/tags a \"b c\"", "a|b c")]
    [InlineData("/tags", "none")]
    public async Task Collections_take_the_remaining_arguments(string text, string expected)
    {
        await using var harness = Harness();

        await harness.ReceiveAsync(text);

        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Mentions_resolve_per_element()
    {
        await using var harness = Harness();
        var account = PhoneNumber.Parse(TestHarness.Account);
        const string text = "/invite club \uFFFC \uFFFC";
        var data = new DataMessage(1, text)
        {
            Mentions = [new Mention("+15550002222", text.IndexOf('\uFFFC'), 1), new Mention("+15550003333", text.LastIndexOf('\uFFFC'), 1)],
        };

        await harness.ReceiveAsync(new IncomingEnvelope(account, new Sender(PhoneNumber.Parse(TestHarness.Alice), null, "Alice"), 1, data));

        Assert.Equal("club: +15550002222,+15550003333", harness.Signal.LastReply);
    }

    [Theory]
    [InlineData(typeof(CollectionNotLastModule), "must be the last positional parameter")]
    [InlineData(typeof(CollectionFlagModule), "can't be a [Flag]")]
    public async Task Invalid_collection_parameters_fail_at_startup(Type module, string message)
    {
        await using var harness = TestHarness.Create(setup: (_, catalog) => catalog.AddModule(module));

        var error = Assert.Throws<InvalidOperationException>(() => harness.Services.GetRequiredService<ICommandRegistry>().Commands);
        Assert.Contains(message, error.Message);
    }
}
