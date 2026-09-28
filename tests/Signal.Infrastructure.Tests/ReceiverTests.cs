using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Receiving;

namespace Signal.Infrastructure.Tests;

public class PollingReceiverTests
{
    [Fact]
    public async Task Polls_receive_endpoint_with_configured_parameters()
    {
        var polls = 0;
        var handler = new StubHandler((_, _) => StubHandler.Json(Interlocked.Increment(ref polls) == 2 ? TestServices.SampleEnvelopes : "[]"));
        await using var provider = TestServices.Build(handler, o =>
        {
            o.Mode = ExecutionMode.Normal;
            o.Receive.TimeoutSeconds = 3;
            o.Receive.SendReadReceipts = true;
            o.Receive.MaxMessages = 10;
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var received = await provider.GetRequiredService<IMessageReceiver>()
            .ReceiveAsync(PhoneNumber.Parse(TestServices.Account), timeout.Token)
            .Take(4)
            .ToListAsync(timeout.Token);

        Assert.Equal("/ping", received[0].Data!.Text);
        Assert.Equal(
            "/v1/receive/%2B15550000000?timeout=3&ignore_attachments=false&ignore_stories=true&send_read_receipts=true&max_messages=10",
            handler.Requests.First().PathAndQuery);
    }

    [Fact]
    public async Task Keeps_polling_after_errors()
    {
        var polls = 0;
        var handler = new StubHandler((_, _) => Interlocked.Increment(ref polls) switch
        {
            1 => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _ => StubHandler.Json(TestServices.SampleEnvelopes),
        });
        await using var provider = TestServices.Build(handler, o => { o.Mode = ExecutionMode.Native; o.Http.RetryCount = 0; });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = await provider.GetRequiredService<IMessageReceiver>()
            .ReceiveAsync(PhoneNumber.Parse(TestServices.Account), timeout.Token)
            .FirstAsync(timeout.Token);

        Assert.Equal("/ping", first.Data!.Text);
        Assert.True(polls >= 2);
    }
}

public class WebSocketReceiverTests
{
    [Theory]
    [InlineData("http://localhost:8080", "ws://localhost:8080/v1/receive/%2B15550000000")]
    [InlineData("https://signal.example.com/api/", "wss://signal.example.com/api/v1/receive/%2B15550000000")]
    public void Builds_websocket_uri(string baseUrl, string expected) =>
        Assert.Equal(expected, WebSocketMessageReceiver.BuildUri(new Uri(baseUrl), PhoneNumber.Parse(TestServices.Account)).AbsoluteUri);

    [Fact]
    public async Task Receives_frames_and_reconnects_after_server_close()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var connector = new LoopbackConnector(((IPEndPoint)listener.LocalEndpoint).Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Each connection pushes one message (split over two frames on the first connection) and closes.
        var server = Task.Run(async () =>
        {
            for (var connection = 1; connection <= 2; connection++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var socket = WebSocket.CreateFromStream(client.GetStream(), new WebSocketCreationOptions { IsServer = true });
                var json = Envelope($"message {connection}");
                if (connection == 1)
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes("not json"), WebSocketMessageType.Text, true, timeout.Token);
                    await socket.SendAsync(json[..10], WebSocketMessageType.Text, false, timeout.Token);
                    await socket.SendAsync(json[10..], WebSocketMessageType.Text, true, timeout.Token);
                }
                else
                {
                    await socket.SendAsync(json, WebSocketMessageType.Text, true, timeout.Token);
                }

                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
        }, timeout.Token);

        await using var provider = TestServices.Build(
            new StubHandler((_, _) => new HttpResponseMessage()),
            o => o.Mode = ExecutionMode.JsonRpc,
            services => services.AddSingleton<IWebSocketConnector>(connector));

        var received = await provider.GetRequiredService<IMessageReceiver>()
            .ReceiveAsync(PhoneNumber.Parse(TestServices.Account), timeout.Token)
            .Take(2)
            .ToListAsync(timeout.Token);
        await server;

        Assert.Equal(["message 1", "message 2"], received.Select(e => e.Data!.Text));
        Assert.Equal(2, connector.Connections);
        Assert.Equal("ws://signal.test:8080/v1/receive/%2B15550000000", connector.LastUri!.AbsoluteUri);
    }

    private static byte[] Envelope(string text) => Encoding.UTF8.GetBytes(
        $$$"""{"envelope":{"sourceNumber":"+15550001111","timestamp":1,"dataMessage":{"timestamp":1,"message":"{{{text}}}"}},"account":"+15550000000"}""");

    private sealed class LoopbackConnector(int port) : IWebSocketConnector
    {
        public int Connections;

        public Uri? LastUri { get; private set; }

        public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            LastUri = uri;
            Interlocked.Increment(ref Connections);
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return WebSocket.CreateFromStream(client.GetStream(), new WebSocketCreationOptions { IsServer = false });
        }
    }
}
