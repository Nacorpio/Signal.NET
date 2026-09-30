using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;
using Signal.Domain;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Http;
using Signal.Infrastructure.Mapping;

namespace Signal.Infrastructure.Receiving;

/// <summary>
/// Receiver for the <c>normal</c> and <c>native</c> modes: repeatedly calls <c>GET /v1/receive/{number}</c>,
/// which returns (and removes) all pending envelopes as a JSON array.
/// </summary>
/// <remarks>
/// After an empty response it waits <see cref="ReceiveOptions.PollingInterval"/>; after a non-empty one it polls
/// again immediately to drain backlogs quickly. Failures are logged and retried with exponential backoff up to
/// <see cref="ReceiveOptions.MaxErrorBackoff"/>. Receive options are re-read every iteration (hot reload).
/// </remarks>
internal sealed partial class PollingMessageReceiver(
    SignalApiClient api,
    IOptionsMonitor<SignalOptions> options,
    TimeProvider time,
    ILogger<PollingMessageReceiver> logger) : ChannelMessageReceiver
{
    protected override async Task ProduceAsync(PhoneNumber account, ChannelWriter<IncomingEnvelope> writer, CancellationToken cancellationToken)
    {
        LogStarted(account.Value, options.CurrentValue.Mode.ContainerValue, options.CurrentValue.Receive.PollingInterval);
        var failures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var receive = options.CurrentValue.Receive;
            try
            {
                var batch = await api.GetAsync(BuildPath(account, receive), SignalEnvelopeJsonContext.Default.ListReceivedMessageDto, cancellationToken);
                failures = 0;

                foreach (var dto in batch)
                {
                    if (EnvelopeMapper.Map(dto, account, includeStories: !receive.IgnoreStories) is { } envelope)
                    {
                        await writer.WriteAsync(envelope, cancellationToken);
                    }
                }

                if (batch.Count == 0)
                {
                    await Task.Delay(receive.PollingInterval, time, cancellationToken);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var delay = Backoff(++failures, receive.PollingInterval, receive.MaxErrorBackoff);
                LogPollFailed(ex, account.Value, failures, delay);
                await Task.Delay(delay, time, cancellationToken);
            }
        }
    }

    /// <summary>Builds the receive path with its query parameters, e.g. <c>v1/receive/%2B49…?timeout=1&amp;…</c>.</summary>
    /// <param name="account">The account.</param>
    /// <param name="receive">The receive options.</param>
    /// <returns>The relative request path.</returns>
    internal static string BuildPath(PhoneNumber account, ReceiveOptions receive)
    {
        var path = new StringBuilder("v1/receive/").Append(SignalApiClient.Escape(account.Value))
            .Append(CultureInfo.InvariantCulture, $"?timeout={receive.TimeoutSeconds}")
            .Append(CultureInfo.InvariantCulture, $"&ignore_attachments={Bool(receive.IgnoreAttachments)}")
            .Append(CultureInfo.InvariantCulture, $"&ignore_stories={Bool(receive.IgnoreStories)}")
            .Append(CultureInfo.InvariantCulture, $"&send_read_receipts={Bool(receive.SendReadReceipts)}");
        if (receive.MaxMessages is { } max)
        {
            path.Append(CultureInfo.InvariantCulture, $"&max_messages={max}");
        }

        return path.ToString();

        static string Bool(bool value) => value ? "true" : "false";
    }

    [LoggerMessage(LogLevel.Information, "Polling messages for {Account} (mode {Mode}, interval {Interval})")]
    private partial void LogStarted(string account, string mode, TimeSpan interval);

    [LoggerMessage(LogLevel.Warning, "Polling messages for {Account} failed ({Failures} in a row); retrying in {Delay}")]
    private partial void LogPollFailed(Exception ex, string account, int failures, TimeSpan delay);
}
