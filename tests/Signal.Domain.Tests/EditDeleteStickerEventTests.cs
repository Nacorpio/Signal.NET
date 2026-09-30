using Signal.Domain.Events;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Tests;

public class EditDeleteStickerEventTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse("+15550000000");
    private static readonly Sender Alice = new(PhoneNumber.Parse("+15550001111"), null, "Alice");

    private static IncomingEnvelope Envelope(EnvelopeContent content) => new(Account, Alice, 1, content);

    [Fact]
    public void Edits_raise_MessageEdited_and_keep_their_group_context()
    {
        var group = GroupId.FromInternalId("abc");
        var edit = new EditMessage(10, new DataMessage(20, "new text") { Group = group });

        var envelope = Envelope(edit);

        var edited = Assert.IsType<MessageEdited>(envelope.ToDomainEvent());
        Assert.Same(edit, edited.Edit);
        Assert.Equal(group, envelope.Group);
        Assert.Equal((Recipient)group, envelope.Conversation);
        Assert.Null(envelope.Data);
    }

    [Fact]
    public void Remote_delete_takes_precedence_and_raises_MessageDeleted()
    {
        var data = new DataMessage(20, null)
        {
            RemoteDelete = new RemoteDelete(10),
            Reaction = new Reaction("👍", "+15550001111", 5, false),
        };

        var deleted = Assert.IsType<MessageDeleted>(Envelope(data).ToDomainEvent());
        Assert.Equal(10, deleted.Delete.TargetTimestamp);
    }

    [Fact]
    public void Transcripts_raise_MessageSent_and_reply_to_their_destination()
    {
        var self = new Sender(Account, null, "Me");
        var bob = PhoneNumber.Parse("+15550002222");
        var transcript = new SentTranscript(bob, new DataMessage(20, "hello bob"));

        var envelope = new IncomingEnvelope(Account, self, 1, transcript);

        var sent = Assert.IsType<MessageSent>(envelope.ToDomainEvent());
        Assert.Same(transcript, sent.Transcript);
        Assert.Equal((Recipient)bob, envelope.Conversation);
        Assert.False(envelope.IsGroup);
    }

    [Fact]
    public void A_sticker_alone_counts_as_content()
    {
        var data = new DataMessage(20, null) { Sticker = new Sticker("abc", 1) };

        Assert.True(data.HasContent);
        Assert.IsType<MessageReceived>(Envelope(data).ToDomainEvent());
    }
}
