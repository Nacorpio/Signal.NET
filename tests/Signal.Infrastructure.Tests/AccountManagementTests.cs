using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

public class AccountManagementTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);

    private static (StubHandler Handler, ServiceProvider Provider) Build(Func<HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler((_, _) => respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NoContent));
        return (handler, TestServices.Build(handler, o => o.Http.RetryCount = 0));
    }

    private static JsonElement Body((HttpMethod Method, string PathAndQuery, string? Body) request) =>
        JsonDocument.Parse(request.Body!).RootElement;

    [Fact]
    public async Task SetUsername_returns_assigned_username_and_link()
    {
        var (handler, provider) = Build(() => StubHandler.Json(
            """{"username":"alice.42","username_link":"https://signal.me/#eu/abc"}""", HttpStatusCode.Created));
        await using var _ = provider;

        var result = await provider.GetRequiredService<IAccountService>().SetUsernameAsync(Account, " alice ");

        Assert.Equal("alice.42", result!.Username.Value);
        Assert.Equal("https://signal.me/#eu/abc", result.Link);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/accounts/%2B15550000000/username", request.PathAndQuery);
        Assert.Equal("alice", Body(request).GetProperty("username").GetString());
    }

    [Fact]
    public async Task SetUsername_handles_204_without_body()
    {
        var (_, provider) = Build();
        await using var __ = provider;

        Assert.Null(await provider.GetRequiredService<IAccountService>().SetUsernameAsync(Account, "alice"));
    }

    [Fact]
    public async Task DeleteUsername_and_RemovePin_use_DELETE()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var accounts = provider.GetRequiredService<IAccountService>();

        await accounts.DeleteUsernameAsync(Account);
        await accounts.RemovePinAsync(Account);

        Assert.Equal(
            [(HttpMethod.Delete, "/v1/accounts/%2B15550000000/username"), (HttpMethod.Delete, "/v1/accounts/%2B15550000000/pin")],
            handler.Requests.Select(r => (r.Method, r.PathAndQuery)));
    }

    [Fact]
    public async Task UpdateSettings_omits_unchanged_values()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IAccountService>().UpdateSettingsAsync(Account, new AccountSettings(DiscoverableByNumber: false));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/v1/accounts/%2B15550000000/settings", request.PathAndQuery);
        Assert.Equal("""{"discoverable_by_number":false}""", request.Body);
    }

    [Fact]
    public async Task SetPin_and_rate_limit_challenge_send_their_bodies()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var accounts = provider.GetRequiredService<IAccountService>();

        await accounts.SetPinAsync(Account, "123456");
        await accounts.SubmitRateLimitChallengeAsync(Account, " token-1 ", " signalcaptcha://abc ");

        var requests = handler.Requests.ToArray();
        Assert.Equal("/v1/accounts/%2B15550000000/pin", requests[0].PathAndQuery);
        Assert.Equal("123456", Body(requests[0]).GetProperty("pin").GetString());
        Assert.Equal("/v1/accounts/%2B15550000000/rate-limit-challenge", requests[1].PathAndQuery);
        Assert.Equal("token-1", Body(requests[1]).GetProperty("challenge_token").GetString());
        Assert.Equal("signalcaptcha://abc", Body(requests[1]).GetProperty("captcha").GetString());
    }

    [Fact]
    public async Task Rejects_empty_arguments_without_calling_the_api()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var accounts = provider.GetRequiredService<IAccountService>();

        await Assert.ThrowsAsync<ArgumentException>(() => accounts.SetUsernameAsync(Account, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.SetPinAsync(Account, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.SubmitRateLimitChallengeAsync(Account, "t", " "));
        Assert.Empty(handler.Requests);
    }

    /// <summary>A custom service written before the account-management members existed.</summary>
    private sealed class LegacyAccountService : IAccountService
    {
        public Task<IReadOnlyList<PhoneNumber>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PhoneNumber>>([]);

        public Task<byte[]> GetLinkQrCodeAsync(string deviceName, CancellationToken cancellationToken = default) => Task.FromResult<byte[]>([]);
    }

    [Fact]
    public async Task Existing_implementations_compile_and_report_unsupported()
    {
        IAccountService legacy = new LegacyAccountService();

        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.SetUsernameAsync(Account, "alice"));
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.UpdateSettingsAsync(Account, new AccountSettings()));
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.SubmitRateLimitChallengeAsync(Account, "t", "c"));
    }
}
