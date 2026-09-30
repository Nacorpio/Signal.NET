using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Events;

/// <summary>
/// Something that happened in a Signal conversation. Raised by <see cref="IncomingEnvelope.ToDomainEvent"/>
/// and delivered to event handlers by the application layer.
/// </summary>
public interface IDomainEvent
{
    /// <summary>The envelope that produced the event (sender, account, conversation, raw content).</summary>
    IncomingEnvelope Envelope { get; }

    /// <summary>When the event happened (the envelope timestamp).</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Base record for the built-in domain events.</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
public abstract record DomainEvent(IncomingEnvelope Envelope) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt => Envelope.ReceivedAt;
}

/// <summary>A message with text, attachments and/or a sticker was received.</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Message">The received message.</param>
public sealed record MessageReceived(IncomingEnvelope Envelope, DataMessage Message) : DomainEvent(Envelope);

/// <summary>
/// The sender edited one of their earlier messages. Edits never run commands; handle this event to react to them.
/// </summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Edit">The edit: the original message's timestamp and the new version.</param>
public sealed record MessageEdited(IncomingEnvelope Envelope, EditMessage Edit) : DomainEvent(Envelope);

/// <summary>
/// The receiving account sent a message from one of its other devices, e.g. typed on the phone. Useful for
/// "note to self" bots and multi-device awareness. Transcripts never run commands.
/// </summary>
/// <param name="Envelope">The envelope that produced the event; its <see cref="IncomingEnvelope.Conversation"/> is the destination.</param>
/// <param name="Transcript">The sent message and its destination.</param>
public sealed record MessageSent(IncomingEnvelope Envelope, SentTranscript Transcript) : DomainEvent(Envelope);

/// <summary>The sender deleted one of their earlier messages for everyone.</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Delete">The deletion, naming the deleted message's timestamp.</param>
public sealed record MessageDeleted(IncomingEnvelope Envelope, RemoteDelete Delete) : DomainEvent(Envelope);

/// <summary>A reaction was added to or removed from a message.</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Reaction">The reaction.</param>
public sealed record ReactionReceived(IncomingEnvelope Envelope, Reaction Reaction) : DomainEvent(Envelope);

/// <summary>A delivery, read or viewed receipt arrived.</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Receipt">The receipt.</param>
public sealed record ReceiptReceived(IncomingEnvelope Envelope, ReceiptMessage Receipt) : DomainEvent(Envelope);

/// <summary>A user started or stopped typing.</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Typing">The typing indicator.</param>
public sealed record TypingIndicatorChanged(IncomingEnvelope Envelope, TypingMessage Typing) : DomainEvent(Envelope);

/// <summary>A group's name, members or settings changed (for example because the account was added to it).</summary>
/// <param name="Envelope">The envelope that produced the event.</param>
/// <param name="Group">The changed group.</param>
public sealed record GroupUpdated(IncomingEnvelope Envelope, GroupId Group) : DomainEvent(Envelope);
