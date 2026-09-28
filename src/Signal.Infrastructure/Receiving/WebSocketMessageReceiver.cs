using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Http;
using Signal.Infrastructure.Mapping;

namespace Signal.Infrastructure.Receiving;

/// <summary>
/// Opens WebSocket connections for <see cref="WebSocketMessageReceiver"/>. Replace it in DI to add headers,
/// authentication, proxies or custom TLS settings, or to connect to an in-memory server in tests.
/// </summary>
public interface IWebSocketConnector
{
    /// <summary>Opens a connected WebSocket.</summary>
    /// <param name="uri">The <c>ws://</c> or <c>wss://</c> receive URI.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>An open WebSocket; the caller disposes it.</returns>
    Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>Default connector using <see cref="ClientWebSocket"/> with the configured keep-alive interval.</summary>
internal sealed class ClientWebSocketConnector(IOptionsMonitor<SignalOptions> options) : IWebSocketConnector
{
    public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = options.CurrentValue.WebSocket.KeepAlive;
        try
        {
            await socket.ConnectAsync(uri, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Receiver for the <c>json-rpc</c> and <c>json-rpc-native</c> modes: <c>/v1/receive/{number}</c> is a WebSocket
/// that pushes one JSON envelope per text message.
/// </summary>
/// <remarks>
/// Messages split over several frames are reassembled; invalid frames are logged and skipped. When the server closes
/// the connection or it fails, the receiver reconnects with exponential backoff between
/// <see cref="WebSocketOptions.ReconnectMinDelay"/> and <see cref="WebSocketOptions.ReconnectMaxDelay"/>; the backoff
/// resets after every successful connection.
/// </remarks>
internal sealed partial class WebSocketMessageReceiver(
    IWebSocketConnector connector,
    IOptionsMonitor<SignalOptions> options,
    TimeProvider time,
    ILogger<WebSocketMessageReceiver> logger) : ChannelMessageReceiver
{
    protected override async Task ProduceAsync(PhoneNumber account, ChannelWriter<IncomingEnvelope> writer, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            var uri = BuildUri(current.BaseUrl, account);
            try
            {
                using var socket = await connector.ConnectAsync(uri, cancellationToken);
                LogConnected(account.Value, uri);
                attempt = 0;
                await ReadAsync(socket, account, writer, current.WebSocket.ReceiveBufferSize, cancellationToken);
                LogClosed(account.Value, socket.CloseStatus, socket.CloseStatusDescription);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogConnectionFailed(ex, account.Value, uri);
            }

            var delay = Backoff(++attempt, current.WebSocket.ReconnectMinDelay, current.WebSocket.ReconnectMaxDelay);
            LogReconnecting(account.Value, delay);
            await Task.Delay(delay, time, cancellationToken);
        }
    }

    /// <summary>Reads messages until the socket closes, reassembling fragmented messages in a pooled buffer.</summary>
    private async Task ReadAsync(WebSocket socket, PhoneNumber account, ChannelWriter<IncomingEnvelope> writer, int bufferSize, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var message = new ArrayBufferWriter<byte>(bufferSize);
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                message.ResetWrittenCount();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseAsync(socket);
                        return;
                    }

                    message.Write(buffer.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text && Parse(message.WrittenSpan, account) is { } envelope)
                {
                    await writer.WriteAsync(envelope, cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Deserializes and maps one message; returns <see langword="null"/> for invalid or unsupported content.</summary>
    private IncomingEnvelope? Parse(ReadOnlySpan<byte> json, PhoneNumber account)
    {
        try
        {
            return JsonSerializer.Deserialize(json, SignalEnvelopeJsonContext.Default.ReceivedMessageDto) is { } dto
                ? EnvelopeMapper.Map(dto, account)
                : null;
        }
        catch (JsonException ex)
        {
            LogInvalidFrame(ex, account.Value);
            return null;
        }
    }

    /// <summary>Completes the close handshake initiated by the server (best effort, 5 s timeout).</summary>
    private static async Task CloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // The connection is going away anyway.
        }
    }

    /// <summary>
    /// Builds the receive URI from the base URL: <c>http</c> becomes <c>ws</c>, <c>https</c> becomes <c>wss</c>,
    /// and a base path (reverse proxy) is preserved, e.g. <c>https://host/api/</c> → <c>wss://host/api/v1/receive/%2B49…</c>.
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="account">The account.</param>
    /// <returns>The WebSocket URI.</returns>
    internal static Uri BuildUri(Uri baseUrl, PhoneNumber account)
    {
        var withSlash = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        var builder = new UriBuilder(new Uri(withSlash, $"v1/receive/{SignalApiClient.Escape(account.Value)}"))
        {
            Scheme = baseUrl.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
        };
        return builder.Uri;
    }

    [LoggerMessage(LogLevel.Information, "WebSocket connected for {Account} ({Uri})")]
    private partial void LogConnected(string account, Uri uri);

    [LoggerMessage(LogLevel.Warning, "WebSocket for {Account} closed by the server ({Status}: {Description})")]
    private partial void LogClosed(string account, WebSocketCloseStatus? status, string? description);

    [LoggerMessage(LogLevel.Warning, "WebSocket connection for {Account} ({Uri}) failed")]
    private partial void LogConnectionFailed(Exception ex, string account, Uri uri);

    [LoggerMessage(LogLevel.Information, "Reconnecting WebSocket for {Account} in {Delay}")]
    private partial void LogReconnecting(string account, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Ignoring an invalid WebSocket frame for {Account}")]
    private partial void LogInvalidFrame(Exception ex, string account);
}
