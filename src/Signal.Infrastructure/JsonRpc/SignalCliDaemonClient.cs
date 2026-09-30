using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Configuration;

namespace Signal.Infrastructure.JsonRpc;

/// <summary>
/// Minimal client for signal-cli's JSON-RPC daemon (newline-delimited JSON over TCP), for features the REST API
/// doesn't expose. Each call uses a short-lived connection: connect, send one request, read until its response.
/// </summary>
/// <remarks>
/// The daemon pushes incoming messages as notifications to every connection. They are skipped here; they are copies,
/// so the REST API's own connection still receives them.
/// </remarks>
internal sealed class SignalCliDaemonClient(IOptionsMonitor<SignalOptions> options)
{
    /// <summary>Whether <c>Signal:JsonRpc:Endpoint</c> is set.</summary>
    public bool IsConfigured => options.CurrentValue.JsonRpc.Endpoint is not null;

    /// <summary>Calls a daemon method and returns its <c>result</c>.</summary>
    /// <param name="method">The method, e.g. <c>joinGroup</c>.</param>
    /// <param name="parameters">String parameters (e.g. <c>account</c>, <c>uri</c>).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A copy of the <c>result</c> element.</returns>
    /// <exception cref="NotSupportedException">No endpoint is configured.</exception>
    /// <exception cref="SignalCliException">The daemon answered with an error.</exception>
    /// <exception cref="TimeoutException">No response within <c>Signal:JsonRpc:Timeout</c>.</exception>
    public async Task<JsonElement> CallAsync(string method, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue.JsonRpc;
        var (host, port) = Split(settings.Endpoint
            ?? throw new NotSupportedException("Set Signal:JsonRpc:Endpoint (e.g. 127.0.0.1:6001) to use signal-cli daemon features."));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, timeout.Token);
            await using var stream = tcp.GetStream();

            var id = Guid.NewGuid().ToString("N");
            await stream.WriteAsync(Request(id, method, parameters), timeout.Token);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (Response(line, id) is not { } response)
                {
                    continue;
                }

                using (response)
                {
                    var root = response.RootElement;
                    if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    {
                        throw new SignalCliException(
                            method,
                            error.TryGetProperty("code", out var code) && code.TryGetInt32(out var value) ? value : 0,
                            error.TryGetProperty("message", out var message) ? message.GetString() : null);
                    }

                    return root.TryGetProperty("result", out var result) ? result.Clone() : default;
                }
            }

            throw new IOException($"The signal-cli daemon closed the connection before answering '{method}'.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The signal-cli daemon did not answer '{method}' within {settings.Timeout}.");
        }
    }

    /// <summary>The request line: <c>{"jsonrpc":"2.0","method":…,"params":{…},"id":…}</c> plus a newline.</summary>
    private static byte[] Request(string id, string method, IReadOnlyDictionary<string, string> parameters)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            writer.WriteStartObject("params");
            foreach (var (name, value) in parameters)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
            writer.WriteString("id", id);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    /// <summary>The parsed line if it is the response with <paramref name="id"/>; notifications and other lines are skipped.</summary>
    private static JsonDocument? Response(string line, string id)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("id", out var responseId)
            && responseId.ValueKind == JsonValueKind.String
            && responseId.GetString() == id)
        {
            return document;
        }

        document.Dispose();
        return null;
    }

    /// <summary>Splits <c>host:port</c>; IPv6 hosts may be bracketed (<c>[::1]:6001</c>).</summary>
    private static (string Host, int Port) Split(string endpoint)
    {
        var colon = endpoint.LastIndexOf(':');
        return (endpoint[..colon].Trim('[', ']'), int.Parse(endpoint[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture));
    }
}
