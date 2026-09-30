using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Background;
using Signal.Application.Configuration;
using Signal.Application.Conversations;
using Signal.Application.Localization;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

public class LocalizationTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestHarness.Account);

    private static readonly Dictionary<string, string> German = new()
    {
        [TextKey.UnknownCommand] = "Unbekannter Befehl '{0}'. Sende {1}help für eine Liste.",
        [TextKey.DidYouMean] = " Meintest du {0}?",
        [TextKey.InvalidArgument] = "'{0}' ist keine gültige {1} für <{2}>.",
        [TextKey.TypePrefix + "whole number"] = "Ganzzahl",
        [TextKey.Usage] = "Verwendung: {0}",
        [TextKey.RequireGroup] = "Nur in Gruppen.",
        [TextKey.HelpHeader] = "Verfügbare Befehle:",
        [TextKey.PromptInvalid] = "'{0}' ist keine gültige {1}. {2}",
    };

    private static async Task<TestHarness> CreateAsync(string? groupCulture, Action<SignalOptions>? configure = null, ConversationSettings? groupSettings = null)
    {
        var harness = TestHarness.Create(
            o =>
            {
                o.Localization.Texts = new() { ["de"] = German };
                configure?.Invoke(o);
            },
            (_, catalog) =>
            {
                catalog.AddModule(typeof(TestModule));
                catalog.AddModule(typeof(PromptModule));
            });
        await harness.Services.GetRequiredService<IConversationSettingsStore>()
            .SetAsync(Account, TestHarness.Group, groupSettings ?? new ConversationSettings { Culture = groupCulture });
        return harness;
    }

    [Theory]
    [InlineData("/ad 1 2", "Unbekannter Befehl 'ad'. Sende /help für eine Liste. Meintest du /add?")]
    [InlineData("/add 1 x", "'x' ist keine gültige Ganzzahl für <b>.\nVerwendung: /add <a> <b>")]
    [InlineData("/help", "Verfügbare Befehle:")]
    public async Task Replies_use_the_conversation_culture(string text, string expected)
    {
        await using var harness = await CreateAsync("de-DE");

        await harness.ReceiveAsync(text, inGroup: true);

        Assert.StartsWith(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Preconditions_are_localised()
    {
        await using var harness = await CreateAsync("de", o => o.Localization.DefaultCulture = "de");

        // A direct message: no conversation settings, so the default culture applies.
        await harness.ReceiveAsync("/grouponly");

        Assert.Equal("Nur in Gruppen.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Other_conversations_and_missing_translations_stay_english()
    {
        await using var harness = await CreateAsync("de-AT");

        await harness.ReceiveAsync("/add 1 2 3");                    // other conversation → English
        var english = harness.Signal.LastReply;
        await harness.ReceiveAsync("/add 1 2 3", inGroup: true);     // de-AT → de, but TooManyArguments isn't translated
        var fallback = harness.Signal.LastReply;

        Assert.StartsWith("Too many arguments", english);
        Assert.Equal("Too many arguments ('3' was not expected).\nVerwendung: /add <a> <b>", fallback);
    }

    [Fact]
    public void Broken_translations_fall_back_to_english()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSignalApplication()
            .Configure<SignalOptions>(o =>
            {
                o.Accounts = [TestHarness.Account];
                o.Localization.Texts = new() { ["de"] = new() { [TextKey.MissingArgument] = "Fehlt: {3}" } };
            })
            .BuildServiceProvider();

        var text = provider.GetRequiredService<ISignalTexts>().Get(TextKey.MissingArgument, System.Globalization.CultureInfo.GetCultureInfo("de"), "b");

        Assert.Equal("Missing argument <b>.", text);
    }

    [Fact]
    public void Unknown_default_cultures_fail_validation()
    {
        var harness = TestHarness.Create(o => o.Localization.DefaultCulture = "xx-nonsense-culture");

        var error = Assert.Throws<OptionsValidationException>(() => harness.Services.GetRequiredService<IOptions<SignalOptions>>().Value);
        Assert.Contains("DefaultCulture", error.Message);
    }

    [Fact]
    public async Task Prompts_ask_again_in_the_conversation_culture_and_respect_its_prefixes()
    {
        await using var harness = await CreateAsync(null, groupSettings: new ConversationSettings { Culture = "de", Prefixes = ["!"] });
        using var stop = new CancellationTokenSource();
        var processing = harness.Services.GetRequiredService<IBackgroundWorkProcessor>().RunAsync(stop.Token);
        var prompts = harness.Services.GetRequiredService<IPromptRegistry>();

        await harness.ReceiveAsync("!ask", inGroup: true);
        await WaitUntilAsync(() => harness.Signal.LastReply == "How many?");

        Assert.False(prompts.TryDeliver(Message("!help")));   // a command in this group
        Assert.True(prompts.TryDeliver(Message("/viele")));   // not a command here, so it's an answer
        await WaitUntilAsync(() => harness.Signal.LastReply == "'/viele' ist keine gültige Ganzzahl. How many?");
        Assert.True(prompts.TryDeliver(Message("3")));
        await WaitUntilAsync(() => harness.Signal.LastReply == "got 3");

        await stop.CancelAsync();
        await processing;
    }

    private static IncomingEnvelope Message(string text) =>
        new(Account, new Sender(PhoneNumber.Parse(TestHarness.Alice), null, "Alice"), 1, new DataMessage(1, text) { Group = TestHarness.Group });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
