using Signal.Domain.Events;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Messaging;

/// <summary>The content of a regular (data) message: text, attachments, reactions, quotes and group context.</summary>
/// <param name="Timestamp">
/// The message timestamp (Unix milliseconds). Together with the author it identifies the message for
/// reactions, quotes, edits and receipts.
/// </param>
/// <param name="Text">The message body, if any.</param>
public sealed record DataMessage(long Timestamp, string? Text)
{
    /// <summary>The group the message was sent in, or <see langword="null"/> for direct messages.</summary>
    public GroupId? Group { get; init; }

    /// <summary>Attachments of the message.</summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    /// <summary>Users mentioned in <see cref="DataMessage.Text"/>.</summary>
    public IReadOnlyList<Mention> Mentions { get; init; } = [];

    /// <summary>The message this message replies to, if any.</summary>
    public Quote? Quote { get; init; }

    /// <summary>Set when the message is a reaction instead of regular content.</summary>
    public Reaction? Reaction { get; init; }

    /// <summary>Whether this message only announces a group change (name, members, settings).</summary>
    public bool IsGroupUpdate { get; init; }

    /// <summary>Whether the message is a view-once message.</summary>
    public bool ViewOnce { get; init; }

    /// <summary>Whether the message carries text or at least one attachment.</summary>
    public bool HasContent => !string.IsNullOrEmpty(Text) || Attachments.Count > 0;
}

/// <summary>A delivery, read or viewed receipt for previously sent messages.</summary>
/// <param name="Type">The kind of receipt.</param>
/// <param name="When">When the receipt was issued (Unix milliseconds).</param>
/// <param name="Timestamps">Timestamps of the acknowledged messages.</param>
public sealed record ReceiptMessage(ReceiptType Type, long When, IReadOnlyList<long> Timestamps);

/// <summary>A typing indicator.</summary>
/// <param name="Action">Whether typing started or stopped.</param>
/// <param name="Timestamp">When the indicator was sent (Unix milliseconds).</param>
/// <param name="Group">The group the user is typing in, or <see langword="null"/> for direct conversations.</param>
public sealed record TypingMessage(TypingAction Action, long Timestamp, GroupId? Group);

/// <summary>
/// What an envelope carries: exactly one of a <see cref="DataMessage"/>, a <see cref="ReceiptMessage"/> or a
/// <see cref="TypingMessage"/>. Signal envelopes never combine them, and the union makes that impossible to violate.
/// </summary>
/// <remarks>
/// Switch over the content to handle every kind; the compiler warns when a case is missing:
/// <code>
/// var summary = envelope.Content switch
/// {
///     DataMessage data =&gt; data.Text,
///     ReceiptMessage receipt =&gt; $"{receipt.Type} receipt",
///     TypingMessage typing =&gt; $"typing {typing.Action}",
/// };
/// </code>
/// </remarks>
public union EnvelopeContent(DataMessage, ReceiptMessage, TypingMessage);

/// <summary>
/// Aggregate root for everything received from Signal. Knows which conversation it belongs to
/// and which domain event it represents.
/// </summary>
/// <param name="Account">The receiving account.</param>
/// <param name="Source">Who sent the envelope.</param>
/// <param name="Timestamp">Server timestamp of the envelope (Unix milliseconds).</param>
/// <param name="Content">What the envelope carries.</param>
public sealed record IncomingEnvelope(PhoneNumber Account, Sender Source, long Timestamp, EnvelopeContent Content)
{
    /// <summary>The content if it is a data message; otherwise <see langword="null"/>.</summary>
    public DataMessage? Data => Content.Value as DataMessage;

    /// <summary>The content if it is a receipt; otherwise <see langword="null"/>.</summary>
    public ReceiptMessage? Receipt => Content.Value as ReceiptMessage;

    /// <summary>The content if it is a typing indicator; otherwise <see langword="null"/>.</summary>
    public TypingMessage? Typing => Content.Value as TypingMessage;

    /// <summary>The group context of a data message or typing indicator; <see langword="null"/> for direct conversations and receipts.</summary>
    public GroupId? Group => Content switch
    {
        DataMessage data => data.Group,
        TypingMessage typing => typing.Group,
        _ => null,
    };

    /// <summary>Whether the envelope belongs to a group conversation.</summary>
    public bool IsGroup => Group is not null;

    /// <summary>Where a reply should go: the group, or the sender for direct messages.</summary>
    public Recipient Conversation => Group is { } group ? group : Source.ToRecipient();

    /// <summary><see cref="Timestamp"/> as a <see cref="DateTimeOffset"/>.</summary>
    public DateTimeOffset ReceivedAt => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);

    /// <summary>
    /// The domain event represented by this envelope: <see cref="ReactionReceived"/>, <see cref="GroupUpdated"/> or
    /// <see cref="MessageReceived"/> for data messages (in that order of precedence), <see cref="ReceiptReceived"/> for
    /// receipts and <see cref="TypingIndicatorChanged"/> for typing indicators.
    /// </summary>
    /// <returns>The event, or <see langword="null"/> for data messages without content (e.g. an empty sync).</returns>
    public IDomainEvent? ToDomainEvent() => Content switch
    {
        DataMessage { Reaction: { } reaction } => new ReactionReceived(this, reaction),
        DataMessage { IsGroupUpdate: true, Group: { } group } => new GroupUpdated(this, group),
        DataMessage { HasContent: true } data => new MessageReceived(this, data),
        DataMessage => null,
        ReceiptMessage receipt => new ReceiptReceived(this, receipt),
        TypingMessage typing => new TypingIndicatorChanged(this, typing),
        null => null,
    };
}
