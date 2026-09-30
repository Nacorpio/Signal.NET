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

    // syncMessage.sentMessage follows signal-cli's JsonSyncDataMessage: data fields unwrapped next to destination*.
    [Fact]
    public void Maps_sent_transcripts_to_their_destination()
    {
        var toNumber = MapOne("""
            "syncMessage":{"sentMessage":{"destination":"+15550002222","destinationNumber":"+15550002222","destinationUuid":null,
              "timestamp":1700000000400,"message":"sent from my phone"}}
            """)!;
        var toUuid = MapOne("""
            "syncMessage":{"sentMessage":{"destination":null,"destinationNumber":null,"destinationUuid":"8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f",
              "timestamp":1700000000400,"message":"hidden number"}}
            """)!;

        var transcript = Assert.IsType<SentTranscript>(toNumber.Content.Value);
        Assert.Equal("sent from my phone", transcript.Message.Text);
        Assert.Equal((Recipient)PhoneNumber.Parse("+15550002222"), transcript.Conversation);
        Assert.Equal(transcript.Conversation, toNumber.Conversation);
        Assert.Null(transcript.EditTargetTimestamp);
        Assert.Null(toNumber.Data);
        Assert.Equal((Recipient)AccountId.Parse("8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f"), toUuid.Transcript!.Conversation);
    }

    [Fact]
    public void Maps_group_and_edit_transcripts()
    {
        var group = MapOne("""
            "syncMessage":{"sentMessage":{"destination":null,"timestamp":1700000000400,"message":"hi all",
              "groupInfo":{"groupId":"abc123==","type":"DELIVER"}}}
            """)!;
        var edit = MapOne("""
            "syncMessage":{"sentMessage":{"destination":"+15550002222","destinationNumber":"+15550002222",
              "editMessage":{"targetSentTimestamp":1700000000000,"dataMessage":{"timestamp":1700000000400,"message":"fixed"}}}}
            """)!;

        Assert.Equal((Recipient)GroupId.FromInternalId("abc123=="), group.Transcript!.Conversation);
        Assert.True(group.IsGroup);
        Assert.Equal(1700000000000, edit.Transcript!.EditTargetTimestamp);
        Assert.Equal("fixed", edit.Transcript.Message.Text);
    }

    [Theory]
    [InlineData(""" "syncMessage":{"readMessages":[{"senderNumber":"+15550002222","timestamp":1}]} """)]
    [InlineData(""" "syncMessage":{"type":"CONTACTS_SYNC"} """)]
    [InlineData(""" "syncMessage":{"sentMessage":{"destination":null,"timestamp":1700000000400,"message":"nowhere"}} """)]
    [InlineData(""" "syncMessage":{"sentMessage":{"destinationNumber":"+15550002222"}} """)]
    public void Drops_unsupported_or_incomplete_sync_messages(string json) => Assert.Null(MapOne(json));

    [Fact]
    public void Maps_text_styles_and_skips_none_unknown_and_empty_ranges()
    {
        var envelope = MapOne("""
            "dataMessage":{"timestamp":1,"message":"bold and secret","textStyles":[
              {"style":"BOLD","start":0,"length":4},{"style":"SPOILER","start":9,"length":6},
              {"style":"NONE","start":0,"length":4},{"style":"SPARKLY","start":0,"length":1},{"style":"ITALIC","start":0,"length":0}]}
            """)!;

        Assert.Equal(
            [new StyledRange(TextStyle.Bold, 0, 4), new StyledRange(TextStyle.Spoiler, 9, 6)],
            envelope.Data!.TextStyles);
    }

    private static IncomingEnvelope? MapStory(string envelopeJson, bool includeStories) =>
        EnvelopeMapper.Map(
            JsonSerializer.Deserialize(
                $$$"""{"account":"+15550000000","envelope":{"sourceNumber":"+15550001111","sourceDevice":1,"timestamp":1700000000500,{{{envelopeJson}}}}}""",
                SignalEnvelopeJsonContext.Default.ReceivedMessageDto)!,
            Account,
            includeStories);

    [Fact]
    public void Stories_are_dropped_unless_included()
    {
        const string json = """ "storyMessage":{"allowsReplies":true,"textAttachment":{"text":"my story","style":"BOLD"}} """;

        Assert.Null(MapStory(json, includeStories: false));
        var story = MapStory(json, includeStories: true)!.Story!;
        Assert.Equal("my story", story.Text);
        Assert.True(story.AllowsReplies);
        Assert.Null(story.Group);
    }

    [Fact]
    public void Maps_group_media_stories_and_drops_empty_ones()
    {
        var media = MapStory("""
            "storyMessage":{"allowsReplies":false,"groupId":"abc123==","fileAttachment":{"id":"att9","contentType":"image/jpeg","size":42}}
            """, includeStories: true)!;

        Assert.Equal(new Attachment("att9", "image/jpeg", null, 42), media.Story!.File);
        Assert.Equal(GroupId.FromInternalId("abc123=="), media.Group);
        Assert.Null(MapStory(""" "storyMessage":{"allowsReplies":true} """, includeStories: true));
    }

    [Fact]
    public void Maps_call_events_with_unsigned_ids()
    {
        // Call ids are unsigned 64-bit values (BigInteger in signal-cli); this one exceeds long.MaxValue.
        var offer = MapOne(""" "callMessage":{"offerMessage":{"id":18446744073709551000,"type":"VIDEO_CALL","opaque":"AAAA"}} """)!;
        var hangup = MapOne(""" "callMessage":{"hangupMessage":{"id":7,"type":"NORMAL","deviceId":1}} """)!;

        Assert.Equal(new CallMessage(CallEventKind.Offer, 18446744073709551000) { IsVideo = true }, offer.Call);
        Assert.Equal(new CallMessage(CallEventKind.Hangup, 7), hangup.Call);
        Assert.Null(hangup.Call!.IsVideo);
        Assert.Equal((Recipient)PhoneNumber.Parse("+15550001111"), offer.Conversation);
    }

    [Fact]
    public void Drops_call_messages_with_only_ice_updates() =>
        Assert.Null(MapOne(""" "callMessage":{"iceUpdateMessages":[{"id":7,"opaque":"AAAA"}]} """));

    [Fact]
    public void Ignores_malformed_stickers_but_keeps_the_message()
    {
        var envelope = MapOne(""" "dataMessage":{"timestamp":1,"message":"hi","sticker":{"packId":"not hex","stickerId":1}} """)!;

        Assert.Null(envelope.Data!.Sticker);
        Assert.Equal("hi", envelope.Data.Text);
    }
}
