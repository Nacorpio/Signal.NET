using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Commands;
using Signal.Application.Configuration;
using Signal.Application.Scheduling;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

/// <summary>A clock the test moves by hand. Timers still use real time (only the dispatch loop test needs them).</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class ReminderModule : CommandModule
{
    [Command("remind")]
    public async Task RemindAsync(int minutes, [Remainder] string text)
    {
        var time = Context.Services.GetRequiredService<TimeProvider>();
        var scheduled = await ScheduleReplyAsync(text, time.GetUtcNow().AddMinutes(minutes));
        await ReplyAsync($"scheduled {scheduled.Id != Guid.Empty}");
    }
}

public class SchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestHarness.Account);
    private static readonly Recipient Alice = PhoneNumber.Parse(TestHarness.Alice);

    private static (TestHarness Harness, ManualClock Clock) Create(Action<SignalOptions>? configure = null)
    {
        var clock = new ManualClock(Start);
        var harness = TestHarness.Create(configure, (services, catalog) =>
        {
            services.AddSingleton<TimeProvider>(clock);
            catalog.AddModule(typeof(ReminderModule));
        });
        return (harness, clock);
    }

    private static ScheduledMessageDispatcher Dispatcher(TestHarness harness) =>
        (ScheduledMessageDispatcher)harness.Services.GetRequiredService<IScheduledMessageDispatcher>();

    [Theory]
    [InlineData(0, 60)]      // exactly due → next hour
    [InlineData(30, 60)]     // half an hour late → still the next hour
    [InlineData(150, 180)]   // 2.5 h of downtime → skips the missed ones, next is 3 h after the original
    public void Next_due_skips_missed_occurrences(int nowMinutes, int expectedMinutes) =>
        Assert.Equal(
            Start.AddMinutes(expectedMinutes),
            ScheduledMessageDispatcher.NextDue(Start, TimeSpan.FromHours(1), Start.AddMinutes(nowMinutes)));

    [Fact]
    public async Task One_off_messages_are_sent_once_when_due()
    {
        var (harness, clock) = Create();
        await using var _ = harness;
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();
        await scheduler.ScheduleAsync(Account, Alice, "stand-up", Start.AddMinutes(5));

        Assert.Equal(0, await Dispatcher(harness).DispatchDueAsync(CancellationToken.None));
        clock.Now = Start.AddMinutes(5);
        Assert.Equal(1, await Dispatcher(harness).DispatchDueAsync(CancellationToken.None));
        Assert.Equal(0, await Dispatcher(harness).DispatchDueAsync(CancellationToken.None));

        var sent = Assert.Single(harness.Signal.Sent);
        Assert.Equal("stand-up", sent.Text);
        Assert.Equal(Alice, Assert.Single(sent.Recipients));
        Assert.Empty(await scheduler.ListAsync(Account));
    }

    [Fact]
    public async Task Recurring_messages_move_to_their_next_future_time()
    {
        var (harness, clock) = Create();
        await using var _ = harness;
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();
        var digest = await scheduler.ScheduleAsync(Account, Alice, "digest", Start, TimeSpan.FromHours(1));

        await Dispatcher(harness).DispatchDueAsync(CancellationToken.None);
        Assert.Equal(Start.AddHours(1), Assert.Single(await scheduler.ListAsync(Account)).DueAt);

        // Five hours of downtime: one catch-up message, not five.
        clock.Now = Start.AddHours(5).AddMinutes(10);
        await Dispatcher(harness).DispatchDueAsync(CancellationToken.None);

        Assert.Equal(2, harness.Signal.Sent.Count);
        Assert.Equal(Start.AddHours(6), Assert.Single(await scheduler.ListAsync(Account)).DueAt);
        Assert.True(await scheduler.CancelAsync(digest.Id));
        Assert.Empty(await scheduler.ListAsync(Account));
    }

    [Fact]
    public async Task Failed_sends_are_retried_after_the_retry_delay()
    {
        var (harness, clock) = Create(o => o.Scheduler.RetryDelay = TimeSpan.FromMinutes(2));
        await using var _ = harness;
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();
        await scheduler.ScheduleAsync(Account, Alice, "flaky", Start);
        harness.Signal.OnSend = _ => throw new InvalidOperationException("network down");

        Assert.Equal(0, await Dispatcher(harness).DispatchDueAsync(CancellationToken.None));
        Assert.Equal(Start.AddMinutes(2), Assert.Single(await scheduler.ListAsync(Account)).DueAt);

        harness.Signal.OnSend = null;
        clock.Now = Start.AddMinutes(1);
        Assert.Equal(0, await Dispatcher(harness).DispatchDueAsync(CancellationToken.None));
        clock.Now = Start.AddMinutes(2);
        Assert.Equal(1, await Dispatcher(harness).DispatchDueAsync(CancellationToken.None));
        Assert.Empty(await scheduler.ListAsync(Account));
    }

    [Fact]
    public async Task A_cancellation_during_sending_wins_over_rescheduling()
    {
        var (harness, _) = Create();
        await using var __ = harness;
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();
        var digest = await scheduler.ScheduleAsync(Account, Alice, "digest", Start, TimeSpan.FromHours(1));

        // Cancelled while its send is in flight; the dispatcher must not re-add it.
        harness.Signal.OnSend = _ => scheduler.CancelAsync(digest.Id).GetAwaiter().GetResult();
        await Dispatcher(harness).DispatchDueAsync(CancellationToken.None);

        Assert.Empty(await scheduler.ListAsync(Account));
    }

    [Fact]
    public async Task Commands_schedule_replies_into_their_conversation()
    {
        var (harness, clock) = Create();
        await using var _ = harness;

        await harness.ReceiveAsync("/remind 10 water the plants");
        clock.Now = Start.AddMinutes(10);
        await Dispatcher(harness).DispatchDueAsync(CancellationToken.None);

        Assert.Equal(["scheduled True", "water the plants"], harness.Signal.Sent.Select(m => m.Text));
        Assert.Equal(Alice, Assert.Single(harness.Signal.Sent.Last().Recipients));
    }

    [Fact]
    public async Task The_dispatch_loop_sends_due_messages()
    {
        var (harness, _) = Create(o => o.Scheduler.PollInterval = TimeSpan.FromMilliseconds(10));
        await using var __ = harness;
        using var stop = new CancellationTokenSource();
        var running = harness.Services.GetRequiredService<IScheduledMessageDispatcher>().RunAsync(stop.Token);

        await harness.Services.GetRequiredService<IMessageScheduler>().ScheduleAsync(Account, Alice, "now", Start);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (harness.Signal.Sent.IsEmpty)
        {
            await Task.Delay(10, timeout.Token);
        }

        await stop.CancelAsync();
        await running;
        Assert.Equal("now", Assert.Single(harness.Signal.Sent).Text);
    }

    [Fact]
    public async Task Invalid_schedules_are_rejected()
    {
        var (harness, _) = Create();
        await using var __ = harness;
        var scheduler = harness.Services.GetRequiredService<IMessageScheduler>();

        await Assert.ThrowsAsync<ArgumentException>(() => scheduler.ScheduleAsync(Account, Alice, " ", Start));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => scheduler.ScheduleAsync(Account, Alice, "x", Start, TimeSpan.FromSeconds(30)));
        await Assert.ThrowsAsync<ArgumentException>(() => scheduler.ScheduleAsync(Account, default, "x", Start));
        Assert.Empty(await scheduler.ListAsync(Account));
    }

    [Fact]
    public void Invalid_scheduler_options_fail_validation()
    {
        var (harness, _) = Create(o => o.Scheduler.PollInterval = TimeSpan.Zero);

        var error = Assert.Throws<OptionsValidationException>(() => harness.Services.GetRequiredService<IOptions<SignalOptions>>().Value);
        Assert.Contains("Scheduler", error.Message);
    }
}
