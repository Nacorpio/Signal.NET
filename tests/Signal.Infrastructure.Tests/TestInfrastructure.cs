using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Configuration;
using Signal.Infrastructure.Http;

namespace Signal.Infrastructure.Tests;

/// <summary>Records requests and answers them with a user-supplied function.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public ConcurrentQueue<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Method, request.RequestUri!.PathAndQuery, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal static class TestServices
{
    public const string Account = "+15550000000";

    public static ServiceProvider Build(StubHandler handler, Action<SignalOptions>? configure = null, Action<IServiceCollection>? setup = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<SignalOptions>().Configure(o =>
        {
            o.BaseUrl = new Uri("http://signal.test:8080");
            o.Accounts = [Account];
            o.Http.Timeout = TimeSpan.FromSeconds(10);
            o.WebSocket.ReconnectMinDelay = TimeSpan.FromMilliseconds(10);
            o.WebSocket.ReconnectMaxDelay = TimeSpan.FromMilliseconds(50);
            o.Receive.PollingInterval = TimeSpan.FromMilliseconds(10);
            configure?.Invoke(o);
        });
        services.AddSignalInfrastructure();
        services.AddHttpClient<SignalApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        setup?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public const string SampleEnvelopes = """
        [
          {
            "envelope": {
              "source": "+15550001111",
              "sourceNumber": "+15550001111",
              "sourceUuid": "8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f",
              "sourceName": "Alice",
              "sourceDevice": 2,
              "timestamp": 1700000000000,
              "dataMessage": {
                "timestamp": 1700000000000,
                "message": "/ping",
                "expiresInSeconds": 0,
                "viewOnce": false,
                "groupInfo": { "groupId": "abc123==", "type": "DELIVER" },
                "attachments": [ { "contentType": "image/png", "filename": "a.png", "id": "att1", "size": 12 } ],
                "mentions": [ { "name": "Bob", "number": "+15550002222", "uuid": "1e2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f", "start": 0, "length": 1 } ],
                "quote": { "id": 1699999999999, "author": "+15550002222", "authorNumber": "+15550002222", "text": "earlier" }
              }
            },
            "account": "+15550000000"
          },
          {
            "envelope": {
              "sourceNumber": "+15550001111",
              "timestamp": 1700000000001,
              "dataMessage": {
                "timestamp": 1700000000001,
                "reaction": { "emoji": "👍", "targetAuthorNumber": "+15550000000", "targetSentTimestamp": 1699999999998, "isRemove": false }
              }
            },
            "account": "+15550000000"
          },
          {
            "envelope": {
              "sourceNumber": "+15550001111",
              "timestamp": 1700000000002,
              "receiptMessage": { "when": 1700000000002, "isDelivery": false, "isRead": true, "isViewed": false, "timestamps": [ 1699999999998 ] }
            },
            "account": "+15550000000"
          },
          {
            "envelope": {
              "sourceUuid": "8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f",
              "timestamp": 1700000000003,
              "typingMessage": { "action": "STARTED", "timestamp": 1700000000003 }
            },
            "account": "+15550000000"
          },
          {
            "envelope": { "sourceNumber": "+15550001111", "timestamp": 1700000000004, "syncMessage": {} },
            "account": "+15550000000"
          }
        ]
        """;
}
