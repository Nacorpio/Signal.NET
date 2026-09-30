using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Background;
using Signal.Application.Commands;
using Signal.Application.Configuration;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

/// <summary>Coordinates the slow command with the test.</summary>
public sealed class SlowWorkGate
{
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public object? MessageScopeMarker { get; set; }
    public object? WorkScopeMarker { get; set; }
}

/// <summary>A scoped service used to tell DI scopes apart.</summary>
public sealed class ScopeMarker;

public sealed class BackgroundModule(SlowWorkGate gate, ScopeMarker marker) : CommandModule
{
    [Command("slow")]
    public async Task SlowAsync()
    {
        gate.MessageScopeMarker = marker;
        await ReplyAsync("started");
        await RunInBackgroundAsync(async work =>
        {
            gate.WorkScopeMarker = work.Services.GetRequiredService<ScopeMarker>();
            await gate.Release.Task.WaitAsync(work.CancellationToken);
            await work.ReplyAsync("finished");
        });
    }

    [Command("fail")]
    public Task FailAsync() => RunInBackgroundAsync(_ => throw new InvalidOperationException("boom")).AsTask();

    [Command("ok")]
    public Task OkAsync() => RunInBackgroundAsync(work => work.ReplyAsync("ok from background")).AsTask();
}

public class BackgroundWorkTests
{
    private static TestHarness Harness(int concurrency = 4) => TestHarness.Create(
        o => o.Background.MaxConcurrency = concurrency,
        (services, catalog) =>
        {
            catalog.AddModule(typeof(BackgroundModule));
            catalog.AddModule(typeof(TestModule));
            services.AddSingleton<SlowWorkGate>();
            services.AddScoped<ScopeMarker>();
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task Slow_work_does_not_hold_up_the_conversation()
    {
        await using var harness = Harness();
        using var stop = new CancellationTokenSource();
        var processing = harness.Services.GetRequiredService<IBackgroundWorkProcessor>().RunAsync(stop.Token);
        var gate = harness.Services.GetRequiredService<SlowWorkGate>();

        // Returns as soon as the work is queued, even though the work is still waiting.
        await harness.ReceiveAsync("/slow");
        await harness.ReceiveAsync("/add 1 2");
        Assert.Equal(["started", "3"], harness.Signal.Sent.Select(m => m.Text));

        gate.Release.SetResult();
        await WaitUntilAsync(() => harness.Signal.Sent.Count == 3);
        var reply = harness.Signal.Sent.Last();
        Assert.Equal("finished", reply.Text);
        Assert.Equal((Recipient)PhoneNumber.Parse(TestHarness.Alice), Assert.Single(reply.Recipients));

        // The work had its own DI scope, not the (already disposed) message scope.
        Assert.NotNull(gate.WorkScopeMarker);
        Assert.NotSame(gate.MessageScopeMarker, gate.WorkScopeMarker);

        stop.Cancel();
        await processing;
    }

    [Fact]
    public async Task Failing_work_is_logged_and_later_work_still_runs()
    {
        await using var harness = Harness(concurrency: 1);
        using var stop = new CancellationTokenSource();
        var processing = harness.Services.GetRequiredService<IBackgroundWorkProcessor>().RunAsync(stop.Token);

        await harness.ReceiveAsync("/fail");
        await harness.ReceiveAsync("/ok");

        await WaitUntilAsync(() => harness.Signal.Sent.Any(m => m.Text == "ok from background"));
        stop.Cancel();
        await processing;
    }

    [Fact]
    public async Task Shutdown_cancels_running_work_and_drops_queued_work()
    {
        await using var harness = Harness(concurrency: 1);
        using var stop = new CancellationTokenSource();
        var processing = harness.Services.GetRequiredService<IBackgroundWorkProcessor>().RunAsync(stop.Token);
        var gate = harness.Services.GetRequiredService<SlowWorkGate>();

        await harness.ReceiveAsync("/slow");             // occupies the only worker until cancelled
        await WaitUntilAsync(() => gate.WorkScopeMarker is not null);
        await harness.ReceiveAsync("/ok");               // queued behind it

        stop.Cancel();
        await processing.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(harness.Signal.Sent, m => m.Text is "finished" or "ok from background");
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(4, 0)]
    public void Invalid_background_options_fail_validation(int concurrency, int capacity)
    {
        var harness = TestHarness.Create(o =>
        {
            o.Background.MaxConcurrency = concurrency;
            o.Background.Capacity = capacity;
        });

        var error = Assert.Throws<OptionsValidationException>(() => harness.Services.GetRequiredService<IOptions<SignalOptions>>().Value);
        Assert.Contains("Background", error.Message);
    }
}
