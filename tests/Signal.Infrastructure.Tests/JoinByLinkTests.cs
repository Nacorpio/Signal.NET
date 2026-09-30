using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

/// <summary>
/// A one-connection stand-in for signal-cli's JSON-RPC daemon. It records the request, first pushes a notification
/// (as the real daemon does for incoming messages), then answers with <paramref name="respond"/>.
/// </summary>
internal sealed class FakeDaemon : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _serving;

    public FakeDaemon(Func<string, string?> respond)
    {
        _listener.Start();
        _serving = ServeAsync(respond);
    }

    public string Endpoint => $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public JsonElement Request { get; private set; }

    private async Task ServeAsync(Func<string, string?> respond)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        var line = await reader.ReadLineAsync();
        Request = JsonDocument.Parse(line!).RootElement.Clone();
        var id = Request.GetProperty("id").GetString()!;

        await writer.WriteLineAsync("""{"jsonrpc":"2.0","method":"receive","params":{"envelope":{"source":"+15550001111"},"account":"+15550000000"}}""");
        if (respond(id) is { } response)
        {
            await writer.WriteLineAsync(response);
        }

        // Keep the connection open until the client closes it (a silent daemon for timeout tests).
        await reader.ReadLineAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await _serving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}

public class JoinByLinkTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);
    private static readonly Uri Link = new("https://signal.group/#CjQKIAbc-_def");

    private static ServiceProvider Build(string? endpoint, TimeSpan? timeout = null, string groupsJson = "[]") =>
        TestServices.Build(new StubHandler((_, _) => StubHandler.Json(groupsJson)), o =>
        {
            o.JsonRpc.Endpoint = endpoint;
            o.JsonRpc.Timeout = timeout ?? TimeSpan.FromSeconds(10);
        });

    [Fact]
    public async Task Joins_through_the_daemon_without_a_group_id()
    {
        await using var daemon = new FakeDaemon(id =>
            $$"""{"jsonrpc":"2.0","result":{"timestamp":1700000000000,"results":[],"groupId":"abc123=="},"id":"{{id}}"}""");
        await using var provider = Build(daemon.Endpoint);

        var result = await provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, Link);

        Assert.Equal(new GroupJoinResult(GroupId.FromInternalId("abc123=="), IsPendingApproval: false), result);
        Assert.Equal("2.0", daemon.Request.GetProperty("jsonrpc").GetString());
        Assert.Equal("joinGroup", daemon.Request.GetProperty("method").GetString());
        Assert.Equal(TestServices.Account, daemon.Request.GetProperty("params").GetProperty("account").GetString());
        Assert.Equal(Link.OriginalString, daemon.Request.GetProperty("params").GetProperty("uri").GetString());
    }

    [Fact]
    public async Task Groups_with_approval_report_a_pending_request()
    {
        await using var daemon = new FakeDaemon(id =>
            $$"""{"jsonrpc":"2.0","result":{"timestamp":1,"results":[],"groupId":"abc123==","onlyRequested":true},"id":"{{id}}"}""");
        await using var provider = Build(daemon.Endpoint);

        var result = await provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, Link);

        Assert.True(result.IsPendingApproval);
    }

    [Fact]
    public async Task Daemon_errors_become_SignalCliExceptions()
    {
        // The exact error a real signal-cli 0.14.5 daemon returns for a malformed link.
        await using var daemon = new FakeDaemon(id =>
            $$"""{"jsonrpc":"2.0","error":{"code":-1,"message":"Group link is invalid: java.net.ProtocolException: Unexpected field encoding: 6.","data":null},"id":"{{id}}"}""");
        await using var provider = Build(daemon.Endpoint);

        var error = await Assert.ThrowsAsync<SignalCliException>(() => provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, Link));

        Assert.Equal(("joinGroup", -1), (error.Method, error.Code));
        Assert.StartsWith("Group link is invalid", error.Error);
    }

    [Fact]
    public async Task A_timeout_without_the_group_is_reported_as_unknown()
    {
        await using var daemon = new FakeDaemon(_ => null);
        await using var provider = Build(daemon.Endpoint, TimeSpan.FromMilliseconds(300));

        var error = await Assert.ThrowsAsync<TimeoutException>(() => provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, Link));
        Assert.Contains("may still complete", error.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_timeout_after_a_successful_join_is_resolved_through_the_group_list(bool member, bool pending)
    {
        // Seen live: joining a 208-member group outlasted the timeout although the join had succeeded.
        await using var daemon = new FakeDaemon(_ => null);
        var groups = $$$"""[{"id":"group.b3RoZXI=","invite_link":"https://signal.group/#other"},{"id":"group.YWJjMTIzPT0=","name":"Big","invite_link":"{{{Link.OriginalString}}}","member":{{{member.ToString().ToLowerInvariant()}}}}]""";
        await using var provider = Build(daemon.Endpoint, TimeSpan.FromMilliseconds(300), groups);

        var result = await provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, Link);

        Assert.Equal(new GroupJoinResult(GroupId.Parse("group.YWJjMTIzPT0="), pending), result);
    }

    [Theory]
    [InlineData("https://example.com/#abc")]
    [InlineData("http://signal.group/#abc")]
    [InlineData("https://signal.group/")]
    public async Task Links_are_validated_before_calling_the_daemon(string link)
    {
        await using var provider = Build("127.0.0.1:1");

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, new Uri(link)));
    }

    [Fact]
    public async Task Without_an_endpoint_the_feature_explains_itself()
    {
        await using var provider = Build(endpoint: null);

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetRequiredService<IGroupService>().JoinByLinkAsync(Account, Link));
        Assert.Contains("Signal:JsonRpc:Endpoint", error.Message);
    }
}
