using Signal.Application.Commands;
using Signal.Application.Reactions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

public sealed class PollModule : CommandModule
{
    [Command("poll")]
    public Task PollAsync() => ReplyAsync("Vote with a reaction");
}

public sealed class VoteModule : ReactionModule
{
    [OnReaction("👍")]
    public Task YesAsync() => ReplyAsync($"yes from {Context.Sender.Identifier}");

    [OnReaction("❤", AnyMessage = true)]
    public Task LoveAsync(Reaction reaction) => ReplyAsync($"love for {reaction.TargetAuthor}");

    [OnReaction("👎", IncludeRemovals = true)]
    public Task NoAsync(ReactionContext context, CancellationToken cancellationToken) =>
        ReplyAsync(context.Reaction.IsRemove ? "no withdrawn" : "no");

    [OnReaction("💥")]
    public Task BoomAsync() => throw new InvalidOperationException("boom");

    [OnReaction("💥")]
    public Task AfterBoomAsync() => ReplyAsync("still running");
}

public sealed class InvalidReactionModule : ReactionModule
{
    [OnReaction("👍")]
    public Task BadAsync(string text) => Task.CompletedTask;
}

public class ReactionCommandTests
{
    private const string Bob = "+15550002222";

    // Built from code points: the variation selector and skin tones are invisible in source.
    private static readonly string HeartWithVariationSelector = "❤" + char.ConvertFromUtf32(0xFE0F);
    private static readonly string ThumbsUpMediumSkinTone = "👍" + char.ConvertFromUtf32(0x1F3FD);

    private static TestHarness Harness(params Type[] reactionModules) => TestHarness.Create(setup: (_, catalog) =>
    {
        catalog.AddModule(typeof(PollModule));
        foreach (var module in reactionModules.DefaultIfEmpty(typeof(VoteModule)))
        {
            catalog.AddReactionModule(module);
        }
    });

    private static Task<Pipeline.MessageContext> ReactAsync(TestHarness harness, string emoji, string targetAuthor = TestHarness.Account, bool remove = false) =>
        harness.ReceiveAsync(new IncomingEnvelope(
            PhoneNumber.Parse(TestHarness.Account),
            new Sender(PhoneNumber.Parse(TestHarness.Alice), null, "Alice"),
            2,
            new DataMessage(2, null) { Reaction = new Reaction(emoji, targetAuthor, 1, remove) }));

    [Fact]
    public async Task Reactions_to_the_bots_messages_run_matching_handlers()
    {
        await using var harness = Harness();

        var context = await ReactAsync(harness, "👍");

        Assert.True(context.IsHandled);
        Assert.Equal($"yes from {TestHarness.Alice}", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Reactions_to_other_messages_need_AnyMessage()
    {
        await using var harness = Harness();

        var ignored = await ReactAsync(harness, "👍", targetAuthor: Bob);
        await ReactAsync(harness, "❤", targetAuthor: Bob);

        Assert.False(ignored.IsHandled);
        Assert.Equal([$"love for {Bob}"], harness.Signal.Sent.Select(m => m.Text));
    }

    [Fact]
    public async Task Removals_only_reach_handlers_that_ask_for_them()
    {
        await using var harness = Harness();

        await ReactAsync(harness, "👍", remove: true);
        await ReactAsync(harness, "👎", remove: true);

        Assert.Equal(["no withdrawn"], harness.Signal.Sent.Select(m => m.Text));
    }

    [Fact]
    public async Task Skin_tones_and_variation_selectors_still_match()
    {
        await using var harness = Harness();

        await ReactAsync(harness, ThumbsUpMediumSkinTone);
        await ReactAsync(harness, HeartWithVariationSelector);

        Assert.Equal([$"yes from {TestHarness.Alice}", $"love for {TestHarness.Account}"], harness.Signal.Sent.Select(m => m.Text));
    }

    [Fact]
    public async Task A_failing_handler_does_not_stop_the_others()
    {
        await using var harness = Harness();

        var context = await ReactAsync(harness, "💥");

        Assert.Equal("still running", harness.Signal.LastReply);
        Assert.True(context.IsHandled);
    }

    [Fact]
    public async Task Unmatched_reactions_and_regular_messages_are_left_alone()
    {
        await using var harness = Harness();

        var reaction = await ReactAsync(harness, "🎉");
        await harness.ReceiveAsync("/poll");

        Assert.False(reaction.IsHandled);
        Assert.Equal(["Vote with a reaction"], harness.Signal.Sent.Select(m => m.Text));
    }

    [Fact]
    public void Invalid_handler_signatures_fail_at_registration()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CommandCatalog().AddReactionModule(typeof(InvalidReactionModule)));

        Assert.Contains("unsupported parameter 'text'", error.Message);
    }

    [Theory]
    [InlineData("👍", "👍")]
    [InlineData("❤", "❤")]
    public void Normalize_is_idempotent(string input, string expected) =>
        Assert.Equal(expected, ReactionMiddleware.Normalize(ReactionMiddleware.Normalize(input)));

    [Fact]
    public void Catalog_rejects_non_reaction_modules() =>
        Assert.Throws<ArgumentException>(() => new CommandCatalog().AddReactionModule(typeof(PollModule)));
}
