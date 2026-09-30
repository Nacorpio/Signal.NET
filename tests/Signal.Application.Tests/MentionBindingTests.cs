using Signal.Application.Commands;
using Signal.Application.Pipeline;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

public sealed class MentionModule : CommandModule
{
    [Command("swap")]
    public Task SwapAsync(Recipient first, PhoneNumber second) => ReplyAsync($"{first.Address}|{second.Value}");

    [Command("who")]
    public Task WhoAsync(Recipient user) => ReplyAsync(user.Address);

    [Command("say")]
    public Task SayAsync([Remainder] string text) => ReplyAsync(text);
}

public class MentionBindingTests
{
    private const char Placeholder = '\uFFFC';
    private const string Bob = "+15550002222";
    private const string Carol = "+15550003333";
    private const string HiddenUuid = "8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f";

    private static TestHarness Harness() => TestHarness.Create(setup: (_, catalog) => catalog.AddModule(typeof(MentionModule)));

    /// <summary>Builds a received message where each <c>@name</c> is replaced by Signal's placeholder plus a mention.</summary>
    private static Task<MessageContext> ReceiveAsync(TestHarness harness, string template, params string[] authors)
    {
        var text = template;
        var mentions = new List<Mention>();
        foreach (var author in authors)
        {
            var start = text.IndexOf("@@", StringComparison.Ordinal);
            text = string.Concat(text.AsSpan(0, start), Placeholder.ToString(), text.AsSpan(start + 2));
            mentions.Add(new Mention(author, start, 1));
        }

        // Signal does not guarantee the order of the mention list; the binder must sort by position.
        mentions.Reverse();
        var account = PhoneNumber.Parse(TestHarness.Account);
        var data = new DataMessage(1, text) { Mentions = mentions };
        return harness.ReceiveAsync(new IncomingEnvelope(account, new Sender(PhoneNumber.Parse(TestHarness.Alice), null, "Alice"), 1, data));
    }

    [Fact]
    public async Task Mentions_bind_in_text_order_to_recipient_and_phone_number_parameters()
    {
        await using var harness = Harness();

        await ReceiveAsync(harness, "/swap @@ @@", Bob, Carol);

        Assert.Equal($"{Bob}|{Carol}", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Leading_whitespace_does_not_shift_mention_positions()
    {
        await using var harness = Harness();

        await ReceiveAsync(harness, "  /who @@", Bob);
        Assert.Equal(Bob, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Mentions_of_users_with_hidden_numbers_bind_as_account_ids()
    {
        await using var harness = Harness();

        await ReceiveAsync(harness, "/who @@", HiddenUuid);

        Assert.Equal(HiddenUuid, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Unresolvable_mentions_fail_instead_of_binding_the_placeholder()
    {
        await using var harness = Harness();

        // A placeholder without a matching mention entry. Recipient.TryParse would accept it as a username.
        await ReceiveAsync(harness, $"/who {Placeholder}");

        Assert.Equal("Could not resolve the @mention for <user>.\nUsage: /who <user>", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Remainder_text_keeps_the_placeholder()
    {
        await using var harness = Harness();

        await ReceiveAsync(harness, "/say hi @@", Bob);

        Assert.Equal($"hi {Placeholder}", harness.Signal.LastReply);
    }
}
