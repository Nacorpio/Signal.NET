using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Signal.Application.Abstractions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Receiving;

/// <summary>
/// Base class for receivers. A background producer (poll loop, socket loop, …) writes into a bounded channel that
/// is exposed as an async stream. When the consumer is slow the channel fills up and back-pressure pauses the
/// producer instead of buffering without limit.
/// </summary>
/// <remarks>
/// Derive from this class to build a custom transport; implement only <see cref="ProduceAsync"/> and register the
/// receiver with <c>ISignalBuilder.UseReceiver&lt;T&gt;()</c>. The producer is cancelled when the consumer stops
/// enumerating; exceptions escaping the producer fault the stream.
/// </remarks>
public abstract class ChannelMessageReceiver : IMessageReceiver
{
    /// <summary>Capacity of the buffer between producer and consumer. Default 256 envelopes.</summary>
    protected virtual int Capacity => 256;

    /// <inheritdoc />
    public async IAsyncEnumerable<IncomingEnvelope> ReceiveAsync(PhoneNumber account, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<IncomingEnvelope>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(async () =>
        {
            try
            {
                await ProduceAsync(account, channel.Writer, stop.Token);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return envelope;
            }
        }
        finally
        {
            // The consumer stopped (cancelled, broke out of the loop, or faulted): stop the producer too.
            await stop.CancelAsync();
            await producer;
        }
    }

    /// <summary>
    /// Writes envelopes to <paramref name="writer"/> until <paramref name="cancellationToken"/> is cancelled.
    /// Implementations should handle transient errors themselves (retry/reconnect with <see cref="Backoff"/>).
    /// </summary>
    /// <param name="account">The account to receive for.</param>
    /// <param name="writer">The channel writer; <c>WriteAsync</c> waits while the buffer is full.</param>
    /// <param name="cancellationToken">Stops producing.</param>
    /// <returns>A task that completes when producing has stopped.</returns>
    protected abstract Task ProduceAsync(PhoneNumber account, ChannelWriter<IncomingEnvelope> writer, CancellationToken cancellationToken);

    /// <summary>Exponential backoff with ±20 % jitter: <c>min * 2^(attempt-1)</c>, capped at <paramref name="max"/>.</summary>
    /// <param name="attempt">The 1-based attempt number.</param>
    /// <param name="min">The delay of the first attempt.</param>
    /// <param name="max">The maximum delay.</param>
    /// <returns>The delay before the next attempt.</returns>
    protected static TimeSpan Backoff(int attempt, TimeSpan min, TimeSpan max)
    {
        var exponential = min.TotalMilliseconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 16));
        var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(Math.Min(exponential * jitter, max.TotalMilliseconds));
    }
}
