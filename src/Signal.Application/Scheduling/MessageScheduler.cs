using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Configuration;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Scheduling;

/// <summary>
/// A text message to send later, optionally repeating (reminders, digests). Only plain values, so any
/// <see cref="IScheduledMessageStore"/> can persist it.
/// </summary>
/// <param name="Id">Identifies the message, e.g. to cancel it.</param>
/// <param name="Account">The sending account.</param>
/// <param name="Recipient">Who receives it (phone number, UUID, username or group).</param>
/// <param name="Text">The message text.</param>
/// <param name="DueAt">When to send it next.</param>
public sealed record ScheduledMessage(Guid Id, PhoneNumber Account, Recipient Recipient, string Text, DateTimeOffset DueAt)
{
    /// <summary>How <see cref="Text"/> is interpreted.</summary>
    public TextMode TextMode { get; init; }

    /// <summary>Repeat interval for recurring messages (at least one minute); <see langword="null"/> to send once.</summary>
    public TimeSpan? RepeatEvery { get; init; }
}

/// <summary>
/// Persists scheduled messages. The default keeps them in memory (lost on restart); implement this port with a
/// database to keep reminders across restarts. Implementations must be thread-safe.
/// </summary>
public interface IScheduledMessageStore
{
    /// <summary>Adds a message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the message is stored.</returns>
    Task AddAsync(ScheduledMessage message, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored message with the same <see cref="ScheduledMessage.Id"/> (e.g. its next due time).</summary>
    /// <param name="message">The updated message.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the message is updated.</returns>
    Task UpdateAsync(ScheduledMessage message, CancellationToken cancellationToken = default);

    /// <summary>Removes a message.</summary>
    /// <param name="id">The message id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> if it existed.</returns>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Gets the messages due at or before <paramref name="now"/>.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The due messages.</returns>
    Task<IReadOnlyList<ScheduledMessage>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Lists the messages scheduled for an account, ordered by due time.</summary>
    /// <param name="account">The sending account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The messages.</returns>
    Task<IReadOnlyList<ScheduledMessage>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);
}

/// <summary>Schedules, cancels and lists messages sent later by the host.</summary>
public interface IMessageScheduler
{
    /// <summary>Schedules a text message.</summary>
    /// <param name="account">The sending account.</param>
    /// <param name="recipient">Who receives it.</param>
    /// <param name="text">The text.</param>
    /// <param name="dueAt">When to send it; a time in the past sends it on the next check.</param>
    /// <param name="repeatEvery">Repeat interval (at least one minute), or <see langword="null"/> to send once.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The scheduled message, with its <see cref="ScheduledMessage.Id"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeatEvery"/> is shorter than one minute.</exception>
    Task<ScheduledMessage> ScheduleAsync(PhoneNumber account, Recipient recipient, string text, DateTimeOffset dueAt, TimeSpan? repeatEvery = null, CancellationToken cancellationToken = default);

    /// <summary>Cancels a scheduled message.</summary>
    /// <param name="id">The message id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> if it was still scheduled.</returns>
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists the messages scheduled for an account, ordered by due time.</summary>
    /// <param name="account">The sending account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The messages.</returns>
    Task<IReadOnlyList<ScheduledMessage>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);
}

/// <summary>Schedules messages into the conversation of a received message.</summary>
public static class SchedulingExtensions
{
    /// <summary>Schedules a reply into this message's conversation, sent from the receiving account.</summary>
    /// <param name="context">The message.</param>
    /// <param name="text">The text.</param>
    /// <param name="dueAt">When to send it.</param>
    /// <param name="repeatEvery">Repeat interval (at least one minute), or <see langword="null"/> to send once.</param>
    /// <returns>The scheduled message; keep its id to cancel it.</returns>
    public static Task<ScheduledMessage> ScheduleReplyAsync(this Pipeline.MessageContext context, string text, DateTimeOffset dueAt, TimeSpan? repeatEvery = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Services.GetRequiredService<IMessageScheduler>()
            .ScheduleAsync(context.Account, context.Conversation, text, dueAt, repeatEvery, context.CancellationToken);
    }
}

/// <summary>Sends due scheduled messages; hosted by <c>AddSignal</c>.</summary>
public interface IScheduledMessageDispatcher
{
    /// <summary>Checks for due messages every <see cref="SchedulerOptions.PollInterval"/> until stopped.</summary>
    /// <param name="stoppingToken">Signals shutdown.</param>
    /// <returns>A task that completes when dispatching stopped.</returns>
    Task RunAsync(CancellationToken stoppingToken);
}

/// <summary>Default <see cref="IMessageScheduler"/> on top of <see cref="IScheduledMessageStore"/>.</summary>
internal sealed class MessageScheduler(IScheduledMessageStore store) : IMessageScheduler
{
    /// <summary>The shortest repeat interval, to prevent accidental message floods.</summary>
    public static readonly TimeSpan MinimumRepeatInterval = TimeSpan.FromMinutes(1);

    public async Task<ScheduledMessage> ScheduleAsync(PhoneNumber account, Recipient recipient, string text, DateTimeOffset dueAt, TimeSpan? repeatEvery = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (recipient.Value is null)
        {
            throw new ArgumentException("A default (empty) recipient cannot be addressed.", nameof(recipient));
        }

        if (repeatEvery is { } every)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(every, MinimumRepeatInterval, nameof(repeatEvery));
        }

        var message = new ScheduledMessage(Guid.NewGuid(), account, recipient, text, dueAt) { RepeatEvery = repeatEvery };
        await store.AddAsync(message, cancellationToken);
        return message;
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default) => store.RemoveAsync(id, cancellationToken);

    public Task<IReadOnlyList<ScheduledMessage>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default) =>
        store.ListAsync(account, cancellationToken);
}

/// <summary>Thread-safe in-memory store; scheduled messages are lost on restart.</summary>
internal sealed class InMemoryScheduledMessageStore : IScheduledMessageStore
{
    private readonly ConcurrentDictionary<Guid, ScheduledMessage> _messages = new();

    public Task AddAsync(ScheduledMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _messages.TryAdd(message.Id, message)
            ? Task.CompletedTask
            : throw new InvalidOperationException($"A scheduled message with id {message.Id} already exists.");
    }

    public Task UpdateAsync(ScheduledMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Only if still scheduled: a cancellation while the message was being sent must win.
        if (_messages.TryGetValue(message.Id, out var current))
        {
            _messages.TryUpdate(message.Id, message, current);
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_messages.TryRemove(id, out _));

    public Task<IReadOnlyList<ScheduledMessage>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduledMessage>>([.. _messages.Values.Where(m => m.DueAt <= now).OrderBy(m => m.DueAt)]);

    public Task<IReadOnlyList<ScheduledMessage>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduledMessage>>([.. _messages.Values.Where(m => m.Account == account).OrderBy(m => m.DueAt)]);
}

/// <summary>
/// Polls the store and sends due messages, each in its own DI scope. One-off messages are removed after sending;
/// recurring ones move to their next future due time, so after downtime a missed occurrence is sent once rather
/// than once per missed interval. Failed sends are retried after <see cref="SchedulerOptions.RetryDelay"/>.
/// </summary>
internal sealed partial class ScheduledMessageDispatcher(
    IScheduledMessageStore store,
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<SignalOptions> options,
    ILogger<ScheduledMessageDispatcher> logger) : IScheduledMessageDispatcher
{
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Scheduler.PollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueAsync(stoppingToken);
                await Task.Delay(interval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // E.g. the store is unavailable; try again on the next tick.
                LogCheckFailed(ex);
                await Task.Delay(interval, time, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>Sends everything due now; returns how many sends succeeded.</summary>
    internal async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var sent = 0;
        foreach (var message in await store.GetDueAsync(now, cancellationToken))
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<IMessageSender>().SendAsync(
                        message.Account,
                        OutgoingMessage.To(message.Recipient).WithText(message.Text, message.TextMode).Build(),
                        cancellationToken);
                }

                sent++;
                if (message.RepeatEvery is { } every)
                {
                    await store.UpdateAsync(message with { DueAt = NextDue(message.DueAt, every, now) }, cancellationToken);
                }
                else
                {
                    await store.RemoveAsync(message.Id, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSendFailed(ex, message.Id, message.Recipient.Address);
                await store.UpdateAsync(message with { DueAt = now + options.Value.Scheduler.RetryDelay }, cancellationToken);
            }
        }

        return sent;
    }

    /// <summary>The first occurrence after <paramref name="now"/>, skipping occurrences missed during downtime.</summary>
    internal static DateTimeOffset NextDue(DateTimeOffset due, TimeSpan every, DateTimeOffset now)
    {
        var missed = now >= due ? (long)((now - due).Ticks / every.Ticks) + 1 : 1;
        return due + TimeSpan.FromTicks(every.Ticks * missed);
    }

    [LoggerMessage(LogLevel.Error, "Checking for due scheduled messages failed")]
    private partial void LogCheckFailed(Exception ex);

    [LoggerMessage(LogLevel.Error, "Sending scheduled message {Id} to {Recipient} failed; retrying later")]
    private partial void LogSendFailed(Exception ex, Guid id, string recipient);
}
