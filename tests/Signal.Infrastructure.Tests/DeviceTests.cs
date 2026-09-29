using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

public class DeviceTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);

    private static (StubHandler Handler, ServiceProvider Provider) Build(Func<HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler((_, _) => respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NoContent));
        return (handler, TestServices.Build(handler, o => o.Http.RetryCount = 0));
    }

    [Fact]
    public async Task List_maps_devices_and_treats_zero_timestamps_as_unknown()
    {
        var (handler, provider) = Build(() => StubHandler.Json("""
            [{"id":1,"name":"","creation_timestamp":0,"last_seen_timestamp":1700000000000},
             {"id":2,"name":"Desktop","creation_timestamp":1690000000000,"last_seen_timestamp":1700000000000}]
            """));
        await using var _ = provider;

        var devices = await provider.GetRequiredService<IDeviceService>().ListAsync(Account);

        Assert.Equal("/v1/devices/%2B15550000000", Assert.Single(handler.Requests).PathAndQuery);
        Assert.Equal(2, devices.Count);
        Assert.True(devices[0].IsPrimary);
        Assert.Null(devices[0].Name);
        Assert.Null(devices[0].Created);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), devices[0].LastSeen);
        Assert.False(devices[1].IsPrimary);
        Assert.Equal("Desktop", devices[1].Name);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1690000000000), devices[1].Created);
    }

    [Fact]
    public async Task Link_posts_the_device_uri()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IDeviceService>().LinkAsync(Account, " sgnl://linkdevice?uuid=abc&pub_key=def ");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/devices/%2B15550000000", request.PathAndQuery);
        Assert.Equal("sgnl://linkdevice?uuid=abc&pub_key=def", JsonDocument.Parse(request.Body!).RootElement.GetProperty("uri").GetString());
    }

    [Fact]
    public async Task Remove_deletes_the_device()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IDeviceService>().RemoveAsync(Account, 3);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/v1/devices/%2B15550000000/3", request.PathAndQuery);
    }

    [Fact]
    public async Task Rejects_invalid_arguments_without_calling_the_api()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var devices = provider.GetRequiredService<IDeviceService>();

        await Assert.ThrowsAsync<ArgumentException>(() => devices.LinkAsync(Account, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => devices.RemoveAsync(Account, LinkedDevice.PrimaryDeviceId));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => devices.RemoveAsync(Account, 0));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetLinkUri_returns_the_raw_uri()
    {
        var (handler, provider) = Build(() => StubHandler.Json("""{"device_link_uri":"sgnl://linkdevice?uuid=abc"}"""));
        await using var _ = provider;

        var uri = await provider.GetRequiredService<IAccountService>().GetLinkUriAsync("My Bot");

        Assert.Equal("sgnl://linkdevice?uuid=abc", uri);
        Assert.Equal("/v1/qrcodelink/raw?device_name=My%20Bot", Assert.Single(handler.Requests).PathAndQuery);
    }

    [Fact]
    public async Task GetLinkUri_fails_on_a_missing_uri()
    {
        var (_, provider) = Build(() => StubHandler.Json("{}"));
        await using var __ = provider;

        var error = await Assert.ThrowsAsync<SignalApiException>(() => provider.GetRequiredService<IAccountService>().GetLinkUriAsync("bot"));
        Assert.Contains("v1/qrcodelink/raw", error.Message);
    }
}
