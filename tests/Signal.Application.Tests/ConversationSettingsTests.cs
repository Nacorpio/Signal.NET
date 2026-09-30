using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Configuration;
using Signal.Application.Conversations;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

/// <summary>Wraps the in-memory store and counts reads.</summary>
public sealed class CountingSettingsStore : IConversationSettingsStore
{
    private readonly IConversationSettingsStore _inner = new InMemoryConversationSettingsStore();

    public int Reads { get; private set; }

    public ValueTask<ConversationSettings?> GetAsync(PhoneNumber account, Recipient conversation, CancellationToken cancellationToken = default)
    {
        Reads++;
        return _inner.GetAsync(account, conversation, cancellationToken);
    }

    public Task SetAsync(PhoneNumber account, Recipient conversation, ConversationSettings settings, CancellationToken cancellationToken = default) =>
        _inner.SetAsync(account, conversation, settings, cancellationToken);

    public Task<bool> RemoveAsync(PhoneNumber account, Recipient conversation, CancellationToken cancellationToken = default) =>
        _inner.RemoveAsync(account, conversation, cancellationToken);
}

public class ConversationSettingsTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestHarness.Account);

    private static async Task<(TestHarness Harness, CountingSettingsStore Store)> CreateAsync(ConversationSettings groupSettings, Action<SignalOptions>? configure = null)
    {
        var store = new CountingSettingsStore();
        var harness = TestHarness.Create(configure, (services, catalog) =>
        {
            services.AddSingleton<IConversationSettingsStore>(store);
            catalog.AddModule(typeof(TestModule));
            catalog.AddModule(typeof(PlaylistModule));
        });
        await store.SetAsync(Account, TestHarness.Group, groupSettings);
        return (harness, store);
    }

    [Fact]
    public async Task A_conversation_can_use_its_own_prefixes()
    {
        var (harness, _) = await CreateAsync(new ConversationSettings { Prefixes = ["!"] });
        await using var __ = harness;

        await harness.ReceiveAsync("!add 1 2", inGroup: true);
        await harness.ReceiveAsync("/add 3 4", inGroup: true);   // not a command in this group
        await harness.ReceiveAsync("/add 5 6");                  // other conversations keep the global prefix
        await harness.ReceiveAsync("!add 7 8");

        Assert.Equal(["3", "11"], harness.Signal.Sent.Select(m => m.Text));
    }

    [Theory]
    [InlineData("/add 1 2")]
    [InlineData("/playlist add song")]     // disabled through its group
    [InlineData("/PL remove 1")]
    public async Task Disabled_commands_are_refused(string text)
    {
        var (harness, _) = await CreateAsync(new ConversationSettings { DisabledCommands = ["ADD", "playlist"] });
        await using var __ = harness;

        await harness.ReceiveAsync(text, inGroup: true);

        Assert.Equal("This command is disabled in this conversation.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Disabled_commands_still_work_elsewhere()
    {
        var (harness, _) = await CreateAsync(new ConversationSettings { DisabledCommands = ["add"] });
        await using var __ = harness;

        await harness.ReceiveAsync("/add 1 2");

        Assert.Equal("3", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Help_and_suggestions_skip_disabled_commands()
    {
        var (harness, _) = await CreateAsync(new ConversationSettings { DisabledCommands = ["add", "playlist remove"] });
        await using var __ = harness;

        await harness.ReceiveAsync("/help", inGroup: true);
        var help = harness.Signal.LastReply!;
        await harness.ReceiveAsync("/help add", inGroup: true);
        var details = harness.Signal.LastReply;
        await harness.ReceiveAsync("/ad 1 2", inGroup: true);
        var suggestion = harness.Signal.LastReply;
        await harness.ReceiveAsync("/playlist", inGroup: true);

        Assert.DoesNotContain("/add", help);
        Assert.DoesNotContain("/playlist remove", help);
        Assert.Contains("/playlist add", help);
        Assert.Equal("Unknown command 'add'.", details);
        Assert.DoesNotContain("Did you mean", suggestion);
        Assert.Equal("/playlist needs a subcommand: add. Send /help playlist for details.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task The_disabled_message_can_be_silenced()
    {
        var (harness, _) = await CreateAsync(new ConversationSettings { DisabledCommands = ["add"] }, o => o.Commands.DisabledCommandMessage = "");
        await using var __ = harness;

        await harness.ReceiveAsync("/add 1 2", inGroup: true);

        Assert.Empty(harness.Signal.Sent);
    }

    [Theory]
    [InlineData("/nope")]       // unknown command: parser, executor and result handler all ask
    [InlineData("/add 1 2")]
    [InlineData("just text")]
    public async Task Settings_are_read_once_per_message(string text)
    {
        var (harness, store) = await CreateAsync(new ConversationSettings());
        await using var __ = harness;

        await harness.ReceiveAsync(text, inGroup: true);

        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task The_store_rejects_invalid_prefixes_and_can_reset_a_conversation()
    {
        var store = new InMemoryConversationSettingsStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(Account, TestHarness.Group, new ConversationSettings { Prefixes = ["! "] }));
        await store.SetAsync(Account, TestHarness.Group, new ConversationSettings { Culture = "de-DE" });
        Assert.Equal("de-DE", (await store.GetAsync(Account, TestHarness.Group))!.Culture);
        Assert.True(await store.RemoveAsync(Account, TestHarness.Group));
        Assert.Null(await store.GetAsync(Account, TestHarness.Group));
    }
}
