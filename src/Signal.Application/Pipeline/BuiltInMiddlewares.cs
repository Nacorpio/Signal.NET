using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;
using Signal.Application.Events;

namespace Signal.Application.Pipeline;

/// <summary>
/// First pipeline step: logs and swallows unhandled exceptions so that one bad message never stops the receiver.
/// Cancellation caused by host shutdown is rethrown.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed partial class ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger) : IMessageMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogUnhandled(ex, context.Envelope.Timestamp, context.Sender.ToString());
        }
    }

    [LoggerMessage(LogLevel.Error, "Unhandled exception while processing envelope {Timestamp} from {Sender}")]
    private partial void LogUnhandled(Exception ex, long timestamp, string sender);
}

/// <summary>Logs every processed envelope with its duration at <see cref="LogLevel.Debug"/>.</summary>
/// <param name="logger">The logger.</param>
public sealed partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IMessageMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        var start = Stopwatch.GetTimestamp();
        await next(context);
        LogProcessed(context.Envelope.Timestamp, context.Sender.ToString(), context.Conversation.Address, context.IsHandled,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    [LoggerMessage(LogLevel.Debug, "Processed envelope {Timestamp} from {Sender} in {Conversation} (handled: {Handled}) in {ElapsedMs:0.0} ms")]
    private partial void LogProcessed(long timestamp, string sender, string conversation, bool handled, double elapsedMs);
}

/// <summary>
/// Applies <see cref="AccessControlOptions"/>: drops own messages, blocked senders, and — when an allow list is
/// configured — everyone not on it. Dropped messages reach neither event handlers nor commands.
/// </summary>
/// <param name="options">Live options (changes apply without restart).</param>
/// <param name="logger">The logger.</param>
public sealed partial class AccessControlMiddleware(IOptionsMonitor<SignalOptions> options, ILogger<AccessControlMiddleware> logger) : IMessageMiddleware
{
    /// <inheritdoc />
    public Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        var access = options.CurrentValue.AccessControl;
        var sender = context.Sender;

        if (access.IgnoreOwnMessages && sender.Number == context.Account)
        {
            return Task.CompletedTask;
        }

        if (access.BlockedSenders.Any(sender.Matches)
            || (access.AllowedSenders.Count > 0 && !access.AllowedSenders.Any(sender.Matches)))
        {
            LogRejected(sender.ToString());
            return Task.CompletedTask;
        }

        return next(context);
    }

    [LoggerMessage(LogLevel.Debug, "Ignoring envelope from {Sender} (access control)")]
    private partial void LogRejected(string sender);
}

/// <summary>Decides whether a sender may send another message. Replace it for distributed or custom limits.</summary>
public interface ISenderRateLimiter
{
    /// <summary>Consumes one permit for the sender.</summary>
    /// <param name="senderKey">The sender identifier (phone number or UUID).</param>
    /// <returns><see langword="true"/> if the message may be processed.</returns>
    bool TryAcquire(string senderKey);
}

/// <summary>
/// In-memory fixed-window limiter configured by <see cref="RateLimitOptions"/>. Stale windows are pruned
/// when more than 10,000 senders are tracked.
/// </summary>
internal sealed class FixedWindowSenderRateLimiter(IOptionsMonitor<SignalOptions> options, TimeProvider time) : ISenderRateLimiter
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string senderKey)
    {
        var limit = options.CurrentValue.RateLimit;
        if (limit.PermitsPerWindow <= 0)
        {
            return true;
        }

        var now = time.GetUtcNow();
        var window = _windows.AddOrUpdate(
            senderKey,
            _ => new Window(now, 1),
            (_, w) => now - w.Start >= limit.Window ? new Window(now, 1) : w with { Count = w.Count + 1 });

        if (_windows.Count > 10_000)
        {
            foreach (var stale in _windows.Where(kv => now - kv.Value.Start >= limit.Window))
            {
                _windows.TryRemove(stale);
            }
        }

        return window.Count <= limit.PermitsPerWindow;
    }

    /// <summary>Start of the current window and the number of messages counted in it.</summary>
    private readonly record struct Window(DateTimeOffset Start, int Count);
}

/// <summary>Drops data messages of senders that exceeded <see cref="RateLimitOptions"/>. Receipts and typing indicators are not limited.</summary>
/// <param name="limiter">The rate limiter.</param>
/// <param name="logger">The logger.</param>
public sealed partial class RateLimitingMiddleware(ISenderRateLimiter limiter, ILogger<RateLimitingMiddleware> logger) : IMessageMiddleware
{
    /// <inheritdoc />
    public Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        if (context.Envelope.Data is null || limiter.TryAcquire(context.Sender.Identifier))
        {
            return next(context);
        }

        LogLimited(context.Sender.ToString());
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "Rate limit exceeded for {Sender}; message dropped")]
    private partial void LogLimited(string sender);
}

/// <summary>
/// Publishes the envelope's domain event (<see cref="Domain.Messaging.IncomingEnvelope.ToDomainEvent"/>) to all
/// registered <see cref="IEventHandler{TEvent}"/>s, then continues with command handling.
/// </summary>
/// <param name="dispatcher">The event dispatcher of the message scope.</param>
public sealed class DomainEventMiddleware(IDomainEventDispatcher dispatcher) : IMessageMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        if (context.Envelope.ToDomainEvent() is { } domainEvent)
        {
            await dispatcher.DispatchAsync(domainEvent, context.CancellationToken);
        }

        await next(context);
    }
}
