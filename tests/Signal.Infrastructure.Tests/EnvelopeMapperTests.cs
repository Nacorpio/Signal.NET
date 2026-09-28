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
}
