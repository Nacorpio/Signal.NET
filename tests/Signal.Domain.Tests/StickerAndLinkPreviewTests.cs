using Signal.Domain.Exceptions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Tests;

public class StickerTests
{
    [Theory]
    [InlineData("ABCdef0123:4", "abcdef0123", 4)]
    [InlineData(" 00ff:0 ", "00ff", 0)]
    public void Parses_pack_and_sticker_id(string text, string packId, int stickerId)
    {
        var sticker = Sticker.Parse(text);

        Assert.Equal(packId, sticker.PackId);
        Assert.Equal(stickerId, sticker.StickerId);
        Assert.Equal($"{packId}:{stickerId}", sticker.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("xyz:1")]
    [InlineData("abc:-1")]
    [InlineData("abc:1:2")]
    [InlineData(":1")]
    public void Rejects_malformed_text(string? text)
    {
        Assert.False(Sticker.TryParse(text, out _));
        Assert.Throws<SignalDomainException>(() => Sticker.Parse(text!));
    }

    [Fact]
    public void Constructor_validates()
    {
        Assert.Throws<SignalDomainException>(() => new Sticker("not-hex", 1));
        Assert.Throws<SignalDomainException>(() => new Sticker("abc", -1));
        Assert.Equal(string.Empty, default(Sticker).ToString());
    }
}

public class OutgoingMessageExtrasTests
{
    private static readonly PhoneNumber Bob = PhoneNumber.Parse("+15550001111");

    [Fact]
    public void A_sticker_alone_is_a_valid_message()
    {
        var message = OutgoingMessage.To(Bob).WithSticker("abc", 2).Build();

        Assert.Equal(new Sticker("abc", 2), message.Sticker);
        Assert.Null(message.Text);
    }

    [Fact]
    public void A_sticker_cannot_be_combined_with_attachments() =>
        Assert.Throws<SignalDomainException>(() => OutgoingMessage.To(Bob).WithSticker("abc", 2).WithAttachment([1], "image/png").Build());

    [Fact]
    public void A_default_sticker_is_rejected() =>
        Assert.Throws<ArgumentException>(() => OutgoingMessage.To(Bob).WithSticker(default(Sticker)));

    [Fact]
    public void Link_preview_requires_its_url_in_the_text()
    {
        var message = OutgoingMessage.To(Bob)
            .WithText("see https://example.com/post")
            .WithLinkPreview("https://example.com/post", "Title", "Description")
            .Build();
        Assert.Equal(new LinkPreview("https://example.com/post", "Title", "Description"), message.LinkPreview);

        Assert.Throws<SignalDomainException>(() => OutgoingMessage.To(Bob)
            .WithText("no link here")
            .WithLinkPreview("https://example.com/post")
            .Build());
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("/relative")]
    public void Link_preview_requires_an_absolute_http_url(string url) =>
        Assert.Throws<SignalDomainException>(() => OutgoingMessage.To(Bob).WithLinkPreview(url));
}
