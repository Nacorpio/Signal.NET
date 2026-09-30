using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Configuration;
using Signal.Application.Pipeline;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Background;

/// <summary>
/// The context of queued background work: its own DI scope, the conversation it was queued from and a shutdown
/// token.
/// </summary>
/// <remarks>
/// The message that queued the work has already been handled and its DI scope disposed. Resolve services from
/// <see cref="Services"/>, never capture scoped services from the message.
/// </remarks>
/// <param name="services">The work item's own scoped service provider.</param>
/// <param name="account">The account the work was queued for.</param>
/// <param name="conversation">Where replies go: the conversation the work was queued from.</param>
/// <param name="cancellationToken">Cancelled when the host shuts down.</param>
/// <param name="sender">Who triggered the work, if it was queued from a message; prompts wait for this sender.</param>
public sealed partial class BackgroundWork(IServiceProvider services, PhoneNumber account, Recipient conversation, CancellationToken cancellationToken, Sender? sender = null)
{
    /// <summary>The work item's own scoped service provider.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>The account the work was queued for; replies are sent from it.</summary>
    public PhoneNumber Account { get; } = account;

    /// <summary>Where replies go.</summary>
    public Recipient Conversation { get; } = conversation;

    /// <summary>Cancelled when the host shuts down.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>Who triggered the work, if it was queued from a message.</summary>
    public Sender? Sender { get; } = sender;

    /// <summary>Sends a text into <see cref="Conversation"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(string text) => ReplyAsync(OutgoingMessage.To(Conversation).WithText(text).Build());

    /// <summary>Sends a prepared message from <see cref="Account"/>; its recipients are used as-is.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(OutgoingMessage message) =>
        Services.GetRequiredService<IMessageSender>().SendAsync(Account, message, CancellationToken);
}

/// <summary>Work queued by <see cref="IBackgroundWorkQueue"/>.</summary>
/// <param name="Account">The account replies are sent from.</param>
/// <param name="Conversation">Where replies go.</param>
/// <param name="Work">The work to run.</param>
public sealed record BackgroundWorkItem(PhoneNumber Account, Recipient Conversation, Func<BackgroundWork, Task> Work)
{
    /// <summary>Who triggered the work, if it was queued from a message; required for prompts.</summary>
    public Sender? Sender { get; init; }
}

/// <summary>
/// Runs work outside the conversation partition that received a message, so a slow command does not hold up the
/// messages after it. Items run concurrently (<see cref="BackgroundOptions.MaxConcurrency"/>), each in its own DI scope.
/// </summary>
/// <remarks>
/// Items are processed by the hosted service registered with <c>AddSignal</c>. Items still queued at shutdown are
/// dropped (and logged), so don't use the queue for work that must survive restarts.
/// </remarks>
public interface IBackgroundWorkQueue
{
    /// <summary>Queues work. Waits while the queue is full (<see cref="BackgroundOptions.Capacity"/>).</summary>
    /// <param name="item">The work.</param>
    /// <param name="cancellationToken">Cancels waiting for space in the queue.</param>
    /// <returns>A task that completes when the item was queued (not when it ran).</returns>
    ValueTask QueueAsync(BackgroundWorkItem item, CancellationToken cancellationToken = default);
}

/// <summary>Queues background work from a message.</summary>
public static class BackgroundWorkExtensions
{
    /// <summary>Queues work that replies into this message's conversation, from the receiving account.</summary>
    /// <param name="context">The message.</param>
    /// <param name="work">The work; resolve services from <see cref="BackgroundWork.Services"/>.</param>
    /// <returns>A task that completes when the work was queued.</returns>
    /// <example>
    /// <code>
    /// await Context.Message.QueueBackgroundWorkAsync(async work =>
    /// {
    ///     var report = await work.Services.GetRequiredService&lt;IReportService&gt;().BuildAsync(work.CancellationToken);
    ///     await work.ReplyAsync(report);
    /// });
    /// </code>
    /// </example>
    public static ValueTask QueueBackgroundWorkAsync(this MessageContext context, Func<BackgroundWork, Task> work)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);
        return context.Services.GetRequiredService<IBackgroundWorkQueue>()
            .QueueAsync(new BackgroundWorkItem(context.Account, context.Conversation, work) { Sender = context.Sender }, context.CancellationToken);
    }
}

/// <summary>The default in-memory queue: a bounded channel read by <see cref="BackgroundWorkProcessor"/>.</summary>
internal sealed class ChannelBackgroundWorkQueue(IOptions<SignalOptions> options) : IBackgroundWorkQueue
{
    private readonly Channel<BackgroundWorkItem> _channel = Channel.CreateBounded<BackgroundWorkItem>(
        new BoundedChannelOptions(options.Value.Background.Capacity) { FullMode = BoundedChannelFullMode.Wait });

    public ChannelReader<BackgroundWorkItem> Reader => _channel.Reader;

    public ValueTask QueueAsync(BackgroundWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _channel.Writer.WriteAsync(item, cancellationToken);
    }
}

/// <summary>Runs queued background work; hosted by <c>AddSignal</c>. Replace it to process work elsewhere.</summary>
public interface IBackgroundWorkProcessor
{
    /// <summary>Processes queued work until <paramref name="stoppingToken"/> is cancelled.</summary>
    /// <param name="stoppingToken">Signals shutdown; also passed to running work.</param>
    /// <returns>A task that completes when processing stopped.</returns>
    Task RunAsync(CancellationToken stoppingToken);
}

/// <summary>
/// Runs queued work with <see cref="BackgroundOptions.MaxConcurrency"/> workers until stopped. Failures are logged
/// and never stop the workers.
/// </summary>
internal sealed partial class BackgroundWorkProcessor(
    ChannelBackgroundWorkQueue queue,
    IServiceScopeFactory scopes,
    IOptions<SignalOptions> options,
    ILogger<BackgroundWorkProcessor> logger) : IBackgroundWorkProcessor
{
    /// <summary>Processes items until <paramref name="stoppingToken"/> is cancelled, then drops what is still queued.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(Enumerable.Range(0, options.Value.Background.MaxConcurrency).Select(_ => WorkAsync(stoppingToken)));

        var dropped = 0;
        while (queue.Reader.TryRead(out _))
        {
            dropped++;
        }

        if (dropped > 0)
        {
            LogDropped(dropped);
        }
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await item.Work(new BackgroundWork(scope.ServiceProvider, item.Account, item.Conversation, stoppingToken, item.Sender));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogFailed(ex, item.Conversation.Address);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(LogLevel.Error, "Background work for {Conversation} failed")]
    private partial void LogFailed(Exception ex, string conversation);

    [LoggerMessage(LogLevel.Warning, "Shutting down: dropped {Count} queued background work item(s)")]
    private partial void LogDropped(int count);
}
