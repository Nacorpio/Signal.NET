using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

public class StickerTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);

    private static (StubHandler Handler, ServiceProvider Provider) Build(Func<HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler((_, _) => respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NoContent));
        return (handler, TestServices.Build(handler, o => o.Http.RetryCount = 0));
    }

    [Fact]
    public async Task Send_includes_sticker_and_link_preview()
    {
        var (handler, provider) = Build(() => StubHandler.Json("""{"timestamp":"1"}""", HttpStatusCode.Created));
        await using var scope = provider;
        var sender = provider.GetRequiredService<IMessageSender>();

        await sender.SendAsync(Account, OutgoingMessage.To(Account).WithSticker("ABC", 3).Build());
        await sender.SendAsync(Account, OutgoingMessage.To(Account)
            .WithText("read https://example.com")
            .WithLinkPreview("https://example.com", "Example", base64Thumbnail: "AQID")
            .Build());

        var requests = handler.Requests.ToArray();
        var sticker = JsonDocument.Parse(requests[0].Body!).RootElement;
        Assert.Equal("abc:3", sticker.GetProperty("sticker").GetString());
        Assert.False(sticker.TryGetProperty("link_preview", out _));
        Assert.False(sticker.TryGetProperty("message", out _));

        var preview = JsonDocument.Parse(requests[1].Body!).RootElement.GetProperty("link_preview");
        Assert.Equal("https://example.com", preview.GetProperty("url").GetString());
        Assert.Equal("Example", preview.GetProperty("title").GetString());
        Assert.Equal("AQID", preview.GetProperty("base64_thumbnail").GetString());
        Assert.False(preview.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task List_maps_sticker_packs()
    {
        var (handler, provider) = Build(() => StubHandler.Json("""
            [{"pack_id":"ABC123","url":"","installed":true,"title":"Cats","author":"Alice"},
             {"pack_id":"","installed":false}]
            """));
        await using var _ = provider;

        var packs = await provider.GetRequiredService<IStickerService>().ListAsync(Account);

        Assert.Equal("/v1/sticker-packs/%2B15550000000", Assert.Single(handler.Requests).PathAndQuery);
        Assert.Equal(new StickerPack("abc123", "Cats", "Alice", true, null), Assert.Single(packs));
    }

    [Fact]
    public async Task Install_posts_id_and_key()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IStickerService>().InstallAsync(Account, " abc ", " def ");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("""{"pack_id":"abc","pack_key":"def"}""", request.Body);
    }

    [Fact]
    public async Task Install_from_share_link_reads_the_fragment()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var stickers = provider.GetRequiredService<IStickerService>();

        await stickers.InstallAsync(Account, new Uri("https://signal.art/addstickers/#pack_id=abc&pack_key=def"));

        Assert.Equal("""{"pack_id":"abc","pack_key":"def"}""", Assert.Single(handler.Requests).Body);
        await Assert.ThrowsAsync<ArgumentException>(() => stickers.InstallAsync(Account, new Uri("https://signal.art/addstickers/#pack_id=abc")));
        await Assert.ThrowsAsync<ArgumentException>(() => stickers.InstallAsync(Account, " ", "def"));
        Assert.Single(handler.Requests);
    }
}
