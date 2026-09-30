using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Background;
using Signal.Application.Commands;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

public sealed class PromptModule : CommandModule
{
    [Command("ask")]
    public Task AskAsync() => RunInBackgroundAsync(async work =>
    {
        var result = await work.PromptAsync<int>("How many?", attempts: 2);
        await work.ReplyAsync(result.IsAnswered ? $"got {result.Value}" : result.Status.ToString());
    }).AsTask();

    [Command("name")]
    public Task NameAsync() => RunInBackgroundAsync(async work =>
    {
        var result = await work.PromptAsync("Your name?", TimeSpan.FromMilliseconds(200));
        await work.ReplyAsync(result.IsAnswered ? $"hi {result.Value}" : result.Status.ToString());
    }).AsTask();
}

public class PromptTests
{
    private const string Bob = "+15550002222";

    private static TestHarness Harness() => TestHarness.Create(setup: (_, catalog) =>
    {
        catalog.AddModule(typeof(PromptModule));
        catalog.AddModule(typeof(TestModule));
    });

    private static IncomingEnvelope Message(string text, string from = TestHarness.Alice, bool inGroup = false) =>
        new(PhoneNumber.Parse(TestHarness.Account), new Sender(PhoneNumber.Parse(from), null, "Test"), 99,
            new DataMessage(99, text) { Group = inGroup ? TestHarness.Group : null });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>Runs the background processor for the duration of a test.</summary>
    private sealed class Running : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _processing;

        public Running(TestHarness harness) =>
            _processing = harness.Services.GetRequiredService<IBackgroundWorkProcessor>().RunAsync(_stop.Token);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _processing;
            _stop.Dispose();
        }
    }

    [Fact]
    public async Task Answers_are_delivered_to_the_waiting_prompt_and_parsed()
    {
        await using var harness = Harness();
        await using var _ = new Running(harness);
        var prompts = harness.Services.GetRequiredService<IPromptRegistry>();

        await harness.ReceiveAsync("/ask");
        await WaitUntilAsync(() => harness.Signal.LastReply == "How many?");

        Assert.True(prompts.TryDeliver(Message(" 5 ")));
        await WaitUntilAsync(() => harness.Signal.LastReply == "got 5");
    }

    [Fact]
    public async Task An_answer_arriving_while_the_question_is_being_sent_is_not_lost()
    {
        await using var harness = Harness();
        await using var _ = new Running(harness);
        var prompts = harness.Services.GetRequiredService<IPromptRegistry>();
        var delivered = false;
        harness.Signal.OnSend = message => delivered |= message.Text == "How many?" && prompts.TryDeliver(Message("9"));

        await harness.ReceiveAsync("/ask");

        await WaitUntilAsync(() => harness.Signal.LastReply == "got 9");
        Assert.True(delivered);
    }

    [Fact]
    public async Task Invalid_answers_are_rejected_and_asked_again()
    {
        await using var harness = Harness();
        await using var _ = new Running(harness);
        var prompts = harness.Services.GetRequiredService<IPromptRegistry>();

        await harness.ReceiveAsync("/ask");
        await WaitUntilAsync(() => harness.Signal.LastReply == "How many?");
        Assert.True(prompts.TryDeliver(Message("lots")));
        await WaitUntilAsync(() => harness.Signal.LastReply == "'lots' is not a valid whole number. How many?");
        Assert.True(prompts.TryDeliver(Message("many")));

        // Two attempts, both invalid.
        await WaitUntilAsync(() => harness.Signal.LastReply == "Invalid");
    }

    [Fact]
    public async Task Only_the_prompted_sender_answers_and_commands_are_never_consumed()
    {
        await using var harness = Harness();
        await using var _ = new Running(harness);
        var prompts = harness.Services.GetRequiredService<IPromptRegistry>();

        await harness.ReceiveAsync("/ask", inGroup: true);
        await WaitUntilAsync(() => harness.Signal.LastReply == "How many?");

        Assert.False(prompts.TryDeliver(Message("3", from: Bob, inGroup: true)));      // someone else in the group
        Assert.False(prompts.TryDeliver(Message("3")));                                // same sender, other conversation
        Assert.False(prompts.TryDeliver(Message("/add 1 2", inGroup: true)));          // a command, processed normally
        Assert.True(prompts.TryDeliver(Message("4", inGroup: true)));
        await WaitUntilAsync(() => harness.Signal.LastReply == "got 4");
    }

    [Fact]
    public async Task Unanswered_prompts_time_out()
    {
        await using var harness = Harness();
        await using var _ = new Running(harness);

        await harness.ReceiveAsync("/name");

        await WaitUntilAsync(() => harness.Signal.LastReply == "TimedOut");
        Assert.False(harness.Services.GetRequiredService<IPromptRegistry>().TryDeliver(Message("late")));
    }

    [Fact]
    public async Task A_newer_prompt_replaces_the_older_one()
    {
        await using var harness = Harness();
        var prompts = harness.Services.GetRequiredService<IPromptRegistry>();
        var account = PhoneNumber.Parse(TestHarness.Account);
        var alice = new Sender(PhoneNumber.Parse(TestHarness.Alice), null, "Alice");
        Recipient conversation = PhoneNumber.Parse(TestHarness.Alice);

        var first = prompts.WaitAsync(account, conversation, alice, TimeSpan.FromSeconds(10), CancellationToken.None);
        var second = prompts.WaitAsync(account, conversation, alice, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(prompts.TryDeliver(Message("answer")));
        Assert.Equal("answer", (await second)!.Data!.Text);
    }

    [Fact]
    public async Task Prompts_need_a_sender()
    {
        await using var harness = Harness();
        await using var scope = harness.Services.CreateAsyncScope();
        var work = new BackgroundWork(scope.ServiceProvider, PhoneNumber.Parse(TestHarness.Account), PhoneNumber.Parse(TestHarness.Alice), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => work.PromptAsync("?"));
    }
}
