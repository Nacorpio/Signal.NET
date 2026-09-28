using Signal.Domain.Events;
using Signal.Domain.Exceptions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Tests;

public class OutgoingMessageTests
{
    private static readonly PhoneNumber Bob = PhoneNumber.Parse("+15550001111");

    [Fact]
    public void Builds_a_complete_message()
    {
        var message = OutgoingMessage.To(Bob, Bob)
            .WithStyledText("hi @bob")
            .WithMention(Bob.Value, 3, 4)
            .WithAttachment([1, 2, 3], "image/png", "a.png")
            .Quoting(42, Bob.Value, "original")
            .AsViewOnce()
            .Build();

        Assert.Single(message.Recipients);
        Assert.Equal(TextMode.Styled, message.TextMode);
        Assert.Equal("data:image/png;filename=a.png;base64,AQID", Assert.Single(message.Attachments));
        Assert.Equal(42, message.Quote!.Timestamp);
        Assert.True(message.ViewOnce);
    }

    [Fact]
    public void Requires_recipient_and_content()
    {
        Assert.Throws<SignalDomainException>(() => OutgoingMessage.Create().WithText("x").Build());
        Assert.Throws<SignalDomainException>(() => OutgoingMessage.To(Bob).Build());
    }

    [Fact]
    public void Rejects_mentions_outside_the_text() =>
        Assert.Throws<SignalDomainException>(() => OutgoingMessage.To(Bob).WithText("hi").WithMention(Bob.Value, 1, 5).Build());
}

public class IncomingEnvelopeTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse("+15550000000");
    private static readonly Sender Alice = new(PhoneNumber.Parse("+15550001111"), null, "Alice");

    [Fact]
    public void Direct_message_replies_to_sender_and_raises_MessageReceived()
    {
        var envelope = new IncomingEnvelope(Account, Alice, 1, new DataMessage(1, "hello"));

        Assert.Equal(Alice.ToRecipient(), envelope.Conversation);
        Assert.IsType<MessageReceived>(envelope.ToDomainEvent());
    }

    [Fact]
    public void Group_message_replies_to_group()
    {
        var group = GroupId.FromInternalId("abc");
        var envelope = new IncomingEnvelope(Account, Alice, 1, new DataMessage(1, "hello") { Group = group });

        Assert.True(envelope.IsGroup);
        Assert.Equal<Recipient>(group, envelope.Conversation);
    }

    [Fact]
    public void Reactions_and_group_updates_raise_their_own_events()
    {
        var reaction = new IncomingEnvelope(Account, Alice, 1,
            new DataMessage(1, null) { Reaction = new Reaction("👍", "+15550000000", 5, false) });
        var update = new IncomingEnvelope(Account, Alice, 1,
            new DataMessage(1, null) { Group = GroupId.FromInternalId("g"), IsGroupUpdate = true });

        Assert.IsType<ReactionReceived>(reaction.ToDomainEvent());
        Assert.IsType<GroupUpdated>(update.ToDomainEvent());
    }

    [Fact]
    public void Content_union_exposes_exactly_one_kind()
    {
        var receipt = new IncomingEnvelope(Account, Alice, 1, new ReceiptMessage(ReceiptType.Read, 2, [1]));
        var typing = new IncomingEnvelope(Account, Alice, 1, new TypingMessage(TypingAction.Started, 3, GroupId.FromInternalId("g")));

        Assert.Null(receipt.Data);
        Assert.NotNull(receipt.Receipt);
        Assert.IsType<ReceiptReceived>(receipt.ToDomainEvent());
        Assert.False(receipt.IsGroup);

        Assert.Null(typing.Data);
        Assert.True(typing.IsGroup);
        Assert.IsType<TypingIndicatorChanged>(typing.ToDomainEvent());
    }

    [Fact]
    public void Data_message_without_content_raises_no_event() =>
        Assert.Null(new IncomingEnvelope(Account, Alice, 1, new DataMessage(1, null)).ToDomainEvent());

    [Fact]
    public void Sender_without_number_is_addressed_by_account_id()
    {
        var uuid = Guid.NewGuid();
        var envelope = new IncomingEnvelope(Account, new Sender(null, uuid, null), 1, new DataMessage(1, "hi"));

        Assert.Equal<Recipient>(new AccountId(uuid), envelope.Conversation);
        Assert.Equal(uuid.ToString("D"), envelope.Conversation.Address);
    }

    [Fact]
    public void Builder_rejects_default_recipient() =>
        Assert.Throws<ArgumentException>(() => OutgoingMessage.To(default(Recipient)));

    [Fact]
    public void Sender_matches_number_or_uuid()
    {
        var uuid = Guid.NewGuid();
        var sender = new Sender(PhoneNumber.Parse("+15550001111"), uuid, null);

        Assert.True(sender.Matches("+1 555 000 1111"));
        Assert.True(sender.Matches(uuid.ToString()));
        Assert.False(sender.Matches("+15550002222"));
    }
}
