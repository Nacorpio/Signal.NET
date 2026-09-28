using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Receiving;

namespace Signal.Infrastructure.Tests;

public class RestAdapterTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);

    [Fact]
    public async Task Sends_messages_as_v2_send_with_snake_case_body()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""{"timestamp":"1700000000123"}""", HttpStatusCode.Created));
        await using var provider = TestServices.Build(handler);
        var sender = provider.GetRequiredService<IMessageSender>();

        var result = await sender.SendAsync(Account, OutgoingMessage.To(PhoneNumber.Parse("+15550001111"), GroupId.FromInternalId("g"))
            .WithStyledText("**hi**")
            .Quoting(42, "+15550001111", "q")
            .Build());

        Assert.Equal(1700000000123, result.Timestamp);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v2/send", request.PathAndQuery);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("+15550000000", root.GetProperty("number").GetString());
        Assert.Equal(["+15550001111", "group.Zw=="], root.GetProperty("recipients").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("styled", root.GetProperty("text_mode").GetString());
        Assert.Equal(42, root.GetProperty("quote_timestamp").GetInt64());
        Assert.False(root.TryGetProperty("base64_attachments", out _));
    }

    [Fact]
    public async Task Does_not_retry_sends_and_surfaces_api_errors()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""{"error":"Unregistered user"}""", HttpStatusCode.BadRequest));
        await using var provider = TestServices.Build(handler);

        var error = await Assert.ThrowsAsync<SignalApiException>(() =>
            provider.GetRequiredService<IMessageSender>().SendAsync(Account, OutgoingMessage.To(Account).WithText("x").Build()));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Unregistered user", error.ApiError);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retries_idempotent_requests_on_server_errors()
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => ++calls < 2
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : StubHandler.Json("""["+15550000000"]"""));
        await using var provider = TestServices.Build(handler, o => o.Http.RetryCount = 2);

        var accounts = await provider.GetRequiredService<IAccountService>().ListAsync();

        Assert.Equal(Account, Assert.Single(accounts));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Maps_groups_and_escapes_path_segments()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""
            [{"name":"Team","id":"group.YWJj","internal_id":"abc","members":["+15550001111"],"admins":["+15550001111"],"blocked":false,"pending_invites":[]}]
            """));
        await using var provider = TestServices.Build(handler);

        var group = Assert.Single(await provider.GetRequiredService<IGroupService>().ListAsync(Account));

        Assert.Equal("Team", group.Name);
        Assert.True(group.IsAdmin("+15550001111"));
        Assert.Equal("/v1/groups/%2B15550000000", Assert.Single(handler.Requests).PathAndQuery);
    }

    [Fact]
    public async Task Reads_about_and_parses_mode()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""
            {"versions":["v1","v2"],"build":2,"mode":"json-rpc","version":"0.92","capabilities":{"v2/send":["quotes","mentions"]}}
            """));
        await using var provider = TestServices.Build(handler);

        var about = await provider.GetRequiredService<ISystemService>().GetAboutAsync();

        Assert.Equal(ExecutionMode.JsonRpc, about.Mode);
        Assert.Equal(["quotes", "mentions"], about.Capabilities["v2/send"]);
    }

    [Theory]
    [InlineData(ExecutionMode.Normal, typeof(PollingMessageReceiver))]
    [InlineData(ExecutionMode.Native, typeof(PollingMessageReceiver))]
    [InlineData(ExecutionMode.JsonRpc, typeof(WebSocketMessageReceiver))]
    [InlineData(ExecutionMode.JsonRpcNative, typeof(WebSocketMessageReceiver))]
    public async Task Selects_receiver_by_execution_mode(ExecutionMode mode, Type expected)
    {
        await using var provider = TestServices.Build(new StubHandler((_, _) => new HttpResponseMessage()), o => o.Mode = mode);

        Assert.IsType(expected, provider.GetRequiredService<IMessageReceiverFactory>().Create(mode));
        Assert.IsType(expected, provider.GetRequiredService<IMessageReceiver>());
    }
}
