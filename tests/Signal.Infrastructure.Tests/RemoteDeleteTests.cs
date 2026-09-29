using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

public class RemoteDeleteTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);

    [Fact]
    public async Task Sends_delete_request_with_recipient_and_target_timestamp()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""{"timestamp":"1700000000999"}""", HttpStatusCode.Created));
        await using var provider = TestServices.Build(handler);
        var group = GroupId.FromInternalId("g");

        var result = await provider.GetRequiredService<IMessageSender>().RemoteDeleteAsync(Account, group, 1700000000123);

        Assert.Equal(1700000000999, result.Timestamp);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/v1/remote-delete/%2B15550000000", request.PathAndQuery);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("group.Zw==", body.RootElement.GetProperty("recipient").GetString());
        Assert.Equal(1700000000123, body.RootElement.GetProperty("timestamp").GetInt64());
    }

    [Fact]
    public async Task Surfaces_api_errors()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""{"error":"message too old"}""", HttpStatusCode.BadRequest));
        await using var provider = TestServices.Build(handler, o => o.Http.RetryCount = 0);

        var error = await Assert.ThrowsAsync<SignalApiException>(() =>
            provider.GetRequiredService<IMessageSender>().RemoteDeleteAsync(Account, Account, 1));

        Assert.Equal("message too old", error.ApiError);
    }

    /// <summary>A custom sender written before RemoteDeleteAsync existed.</summary>
    private sealed class LegacySender : IMessageSender
    {
        public Task<SendResult> SendAsync(PhoneNumber account, OutgoingMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SendResult(1));
    }

    [Fact]
    public async Task Existing_implementations_compile_and_report_unsupported()
    {
        IMessageSender legacy = new LegacySender();

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => legacy.RemoteDeleteAsync(Account, Account, 1));

        Assert.Contains(nameof(LegacySender), error.Message);
    }
}
