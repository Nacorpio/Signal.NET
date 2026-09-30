using System.Text.Json;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Http;
using Signal.Infrastructure.Mapping;

namespace Signal.Infrastructure.Tests;

public class EnvelopeMapperTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);

    private static List<IncomingEnvelope> MapSample() =>
        [.. JsonSerializer.Deserialize(TestServices.SampleEnvelopes, SignalEnvelopeJsonContext.Default.ListReceivedMessageDto)!
            .Select(dto => EnvelopeMapper.Map(dto, Account))
            .OfType<IncomingEnvelope>()];

    [Fact]
    public void Drops_unsupported_envelopes() => Assert.Equal(4, MapSample().Count);

    [Fact]
    public void Maps_data_messages_with_group_attachments_mentions_and_quote()
    {
        var envelope = MapSample()[0];
        var data = envelope.Data!;

        Assert.Equal("+15550001111", envelope.Source.Number!.Value.Value);
        Assert.Equal(Guid.Parse("8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f"), envelope.Source.Uuid);
        Assert.Equal("Alice", envelope.Source.Name);
        Assert.Equal(2, envelope.Source.Device);
        Assert.Equal("/ping", data.Text);
        Assert.Equal(GroupId.FromInternalId("abc123=="), data.Group);
        Assert.False(data.IsGroupUpdate);
        Assert.Equal(new Attachment("att1", "image/png", "a.png", 12), Assert.Single(data.Attachments));
        Assert.Equal("+15550002222", Assert.Single(data.Mentions).Author);
        Assert.Equal(new Quote(1699999999999, "+15550002222", "earlier"), data.Quote);
        Assert.True(envelope.IsGroup);
    }

    [Fact]
    public void Maps_reactions_receipts_and_typing()
    {
        var sample = MapSample();

        Assert.Equal(new Reaction("👍", "+15550000000", 1699999999998, false), sample[1].Data!.Reaction);
        Assert.Equal(ReceiptType.Read, sample[2].Receipt!.Type);
        Assert.Equal([1699999999998L], sample[2].Receipt!.Timestamps);
        Assert.Equal(TypingAction.Started, sample[3].Typing!.Action);
        Assert.Null(sample[3].Source.Number);
    }

    // Shapes follow signal-cli's JsonMessageEnvelope / JsonEditMessage / JsonDataMessage records.
    private static IncomingEnvelope? MapOne(string envelopeJson) =>
        EnvelopeMapper.Map(
            JsonSerializer.Deserialize(
                $$$"""{"account":"+15550000000","envelope":{"sourceNumber":"+15550001111","sourceDevice":1,"timestamp":1700000000500,{{{envelopeJson}}}}}""",
                SignalEnvelopeJsonContext.Default.ReceivedMessageDto)!,
            Account);

    [Fact]
    public void Maps_edits_with_their_target_and_group()
    {
        var envelope = MapOne("""
            "editMessage":{"targetSentTimestamp":1700000000000,
              "dataMessage":{"timestamp":1700000000400,"message":"fixed typo","groupInfo":{"groupId":"abc123==","type":"DELIVER"}}}
            """)!;

        var edit = Assert.IsType<EditMessage>(envelope.Content.Value);
        Assert.Equal(1700000000000, edit.TargetTimestamp);
        Assert.Equal("fixed typo", edit.Message.Text);
        Assert.Equal(1700000000400, edit.Message.Timestamp);
        Assert.Same(edit, envelope.Edit);
        Assert.Null(envelope.Data);
        Assert.Equal(GroupId.FromInternalId("abc123=="), envelope.Group);
    }

    [Theory]
    [InlineData(""" "editMessage":{"targetSentTimestamp":0,"dataMessage":{"timestamp":1,"message":"x"}} """)]
    [InlineData(""" "editMessage":{"targetSentTimestamp":1700000000000} """)]
    public void Drops_malformed_edits(string json) => Assert.Null(MapOne(json));

    [Fact]
    public void Maps_remote_deletes_and_stickers()
    {
        var delete = MapOne(""" "dataMessage":{"timestamp":1700000000400,"message":null,"remoteDelete":{"timestamp":1700000000000}} """)!;
        var sticker = MapOne(""" "dataMessage":{"timestamp":1700000000400,"message":null,"sticker":{"packId":"ABCDEF","stickerId":7}} """)!;

        Assert.Equal(new RemoteDelete(1700000000000), delete.Data!.RemoteDelete);
        Assert.False(delete.Data.HasContent);
        Assert.Equal(new Sticker("abcdef", 7), sticker.Data!.Sticker);
        Assert.True(sticker.Data.HasContent);
    }

    [Fact]
    public void Ignores_malformed_stickers_but_keeps_the_message()
    {
        var envelope = MapOne(""" "dataMessage":{"timestamp":1,"message":"hi","sticker":{"packId":"not hex","stickerId":1}} """)!;

        Assert.Null(envelope.Data!.Sticker);
        Assert.Equal("hi", envelope.Data.Text);
    }
}
