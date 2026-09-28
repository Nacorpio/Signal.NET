using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Configuration;
using Signal.Application.Pipeline;
using Signal.Domain;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Hosting;

/// <summary>
/// The engine of Signal.NET: runs one receive loop per account and processes envelopes through the
/// <see cref="IMessagePipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// Envelopes are partitioned by conversation into <see cref="SignalOptions.MaxConcurrency"/> bounded channels, each
/// with a single worker: different conversations run in parallel, messages of one conversation stay in order.
/// Every envelope is processed in its own DI scope.
/// </para>
/// <para>
/// On startup the container mode is verified (<see cref="SignalOptions.VerifyModeOnStartup"/>). Receivers restart
/// after unexpected failures; on shutdown the receive loops stop and the workers drain their channels.
/// </para>
/// </remarks>
internal sealed partial class SignalHostedService(
    IServiceScopeFactory scopes,
    IMessageReceiverFactory receivers,
    IMessagePipeline pipeline,
    IOptions<SignalOptions> options,
    ILogger<SignalHostedService> logger) : BackgroundService
{
    /// <summary>Verifies the mode, starts the workers and the receive loops, and drains on shutdown.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        await VerifyModeAsync(settings, stoppingToken);

        var accounts = settings.Accounts.Select(PhoneNumber.Parse).Distinct().ToArray();
        var receiver = receivers.Create(settings.Mode);
        LogStarting(accounts.Length, settings.Mode.ContainerValue, settings.Mode.IsStreaming ? "WebSocket" : "HTTP polling", settings.MaxConcurrency);

        var partitions = Enumerable.Range(0, settings.MaxConcurrency)
            .Select(_ => Channel.CreateBounded<IncomingEnvelope>(new BoundedChannelOptions(64) { SingleReader = true }))
            .ToArray();
        var consumers = partitions.Select(p => ConsumeAsync(p.Reader, stoppingToken)).ToArray();

        try
        {
            await Task.WhenAll(accounts.Select(a => ReceiveAsync(receiver, a, partitions, stoppingToken)));
        }
        finally
        {
            foreach (var partition in partitions)
            {
                partition.Writer.TryComplete();
            }

            await Task.WhenAll(consumers);
            LogStopped();
        }
    }

    /// <summary>Receives for one account and routes each envelope to the partition of its conversation; restarts the receiver after failures.</summary>
    private async Task ReceiveAsync(IMessageReceiver receiver, PhoneNumber account, Channel<IncomingEnvelope>[] partitions, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var envelope in receiver.ReceiveAsync(account, stoppingToken))
                {
                    var partition = (int)((uint)StringComparer.Ordinal.GetHashCode(envelope.Conversation.Address) % (uint)partitions.Length);
                    await partitions[partition].Writer.WriteAsync(envelope, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogReceiverFailed(ex, account.Value);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>Worker of one partition: processes envelopes sequentially, each in a new DI scope. Errors are logged, never fatal.</summary>
    private async Task ConsumeAsync(ChannelReader<IncomingEnvelope> reader, CancellationToken stoppingToken)
    {
        await foreach (var envelope in reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await pipeline.ExecuteAsync(new MessageContext(envelope, scope.ServiceProvider, stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down; remaining envelopes are drained without processing delays.
            }
            catch (Exception ex)
            {
                LogProcessingFailed(ex, envelope.Timestamp);
            }
        }
    }

    /// <summary>Compares <c>/v1/about</c> with <see cref="SignalOptions.Mode"/>; warns or throws on mismatch, only warns if the API is unreachable.</summary>
    private async Task VerifyModeAsync(SignalOptions settings, CancellationToken cancellationToken)
    {
        if (!settings.VerifyModeOnStartup)
        {
            return;
        }

        SignalApiInfo info;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            info = await scope.ServiceProvider.GetRequiredService<ISystemService>().GetAboutAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogAboutFailed(ex, settings.BaseUrl);
            return;
        }

        LogApiInfo(info.Version, info.Build, info.RawMode);
        if (info.Mode is { } actual && actual != settings.Mode)
        {
            var message = $"Signal:Mode is {settings.Mode} ({settings.Mode.ContainerValue}) but the container runs in '{info.RawMode}' mode.";
            if (settings.FailOnModeMismatch)
            {
                throw new InvalidOperationException(message);
            }

            LogModeMismatch(message);
        }
    }

    [LoggerMessage(LogLevel.Information, "Signal receiver starting for {Accounts} account(s), mode {Mode} via {Transport}, concurrency {Concurrency}")]
    private partial void LogStarting(int accounts, string mode, string transport, int concurrency);

    [LoggerMessage(LogLevel.Information, "Signal receiver stopped")]
    private partial void LogStopped();

    [LoggerMessage(LogLevel.Error, "Receiver for {Account} failed; restarting in 5 seconds")]
    private partial void LogReceiverFailed(Exception ex, string account);

    [LoggerMessage(LogLevel.Error, "Processing envelope {Timestamp} failed")]
    private partial void LogProcessingFailed(Exception ex, long timestamp);

    [LoggerMessage(LogLevel.Warning, "Could not read /v1/about from {BaseUrl}; skipping mode verification")]
    private partial void LogAboutFailed(Exception ex, Uri baseUrl);

    [LoggerMessage(LogLevel.Information, "Connected to signal-cli-rest-api {Version} (build {Build}, mode {Mode})")]
    private partial void LogApiInfo(string? version, int build, string? mode);

    [LoggerMessage(LogLevel.Warning, "{Message}")]
    private partial void LogModeMismatch(string message);
}
