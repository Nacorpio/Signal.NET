namespace Signal.Infrastructure.Http.Dtos;

// Received messages use signal-cli's own JSON format (camelCase, see SignalEnvelopeJsonContext). The format is
// identical for HTTP polling (a JSON array of these objects) and the json-rpc WebSocket (one object per frame).
// Only the subset Signal.NET models is declared; unknown properties (syncMessage, callMessage, storyMessage, …)
// are ignored. EnvelopeMapper translates these DTOs into IncomingEnvelope.

/// <summary>One received item: the envelope plus the account that received it.</summary>
internal sealed class ReceivedMessageDto
{
    public EnvelopeDto? Envelope { get; set; }

    /// <summary>The receiving account (E.164).</summary>
    public string? Account { get; set; }
}

/// <summary>The signal-cli envelope: sender information plus at most one kind of content.</summary>
internal sealed class EnvelopeDto
{
    /// <summary>Legacy source field: a phone number or UUID.</summary>
    public string? Source { get; set; }

    public string? SourceNumber { get; set; }
    public string? SourceUuid { get; set; }
    public string? SourceName { get; set; }
    public int SourceDevice { get; set; }
    public long Timestamp { get; set; }
    public DataMessageDto? DataMessage { get; set; }
    public EditMessageDto? EditMessage { get; set; }
    public SyncMessageDto? SyncMessage { get; set; }
    public ReceiptMessageDto? ReceiptMessage { get; set; }
    public TypingMessageDto? TypingMessage { get; set; }
}

/// <summary>A regular message. Not sealed: <see cref="SyncSentMessageDto"/> carries the same fields.</summary>
internal class DataMessageDto
{
    public long Timestamp { get; set; }
    public string? Message { get; set; }
    public bool ViewOnce { get; set; }
    public GroupInfoDto? GroupInfo { get; set; }
    public List<AttachmentDto>? Attachments { get; set; }
    public List<IncomingMentionDto>? Mentions { get; set; }
    public QuoteDto? Quote { get; set; }
    public ReactionDto? Reaction { get; set; }
    public StickerDto? Sticker { get; set; }
    public RemoteDeleteDto? RemoteDelete { get; set; }
}

/// <summary>
/// A sync message from another device of the receiving account (signal-cli <c>JsonSyncMessage</c>). Only
/// <see cref="SentMessage"/> is modelled; read, blocked and contact/group sync messages are ignored.
/// </summary>
internal sealed class SyncMessageDto
{
    public SyncSentMessageDto? SentMessage { get; set; }
}

/// <summary>
/// A message sent from another device (signal-cli <c>JsonSyncDataMessage</c>). signal-cli unwraps the data message into
/// this object (<c>@JsonUnwrapped</c>), so its fields are inherited; they are absent when only an edit was sent.
/// </summary>
internal sealed class SyncSentMessageDto : DataMessageDto
{
    /// <summary>Legacy destination field: a phone number or UUID. <see langword="null"/> for group messages.</summary>
    public string? Destination { get; set; }

    public string? DestinationNumber { get; set; }
    public string? DestinationUuid { get; set; }
    public EditMessageDto? EditMessage { get; set; }
}

/// <summary>An edit: the edited message's timestamp and the new version (signal-cli <c>JsonEditMessage</c>).</summary>
internal sealed class EditMessageDto
{
    public long TargetSentTimestamp { get; set; }
    public DataMessageDto? DataMessage { get; set; }
}

/// <summary>A received sticker (signal-cli <c>JsonSticker</c>); the pack id is hex.</summary>
internal sealed class StickerDto
{
    public string? PackId { get; set; }
    public int StickerId { get; set; }
}

/// <summary>A deletion for everyone; <see cref="Timestamp"/> is the deleted message's timestamp.</summary>
internal sealed class RemoteDeleteDto
{
    public long Timestamp { get; set; }
}

/// <summary>Group context of a message. <see cref="GroupId"/> is the internal id; <see cref="Type"/> is <c>DELIVER</c> or <c>UPDATE</c>.</summary>
internal sealed class GroupInfoDto
{
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? Type { get; set; }
}

/// <summary>Metadata of a received attachment.</summary>
internal sealed class AttachmentDto
{
    public string? Id { get; set; }
    public string? ContentType { get; set; }
    public string? Filename { get; set; }
    public long? Size { get; set; }
}

/// <summary>A mention in a received message.</summary>
internal sealed class IncomingMentionDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Uuid { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
}

/// <summary>A quoted message. <see cref="Id"/> is the quoted message's timestamp.</summary>
internal sealed class QuoteDto
{
    public long Id { get; set; }
    public string? Author { get; set; }
    public string? AuthorNumber { get; set; }
    public string? AuthorUuid { get; set; }
    public string? Text { get; set; }
}

/// <summary>A reaction.</summary>
internal sealed class ReactionDto
{
    public string? Emoji { get; set; }
    public string? TargetAuthor { get; set; }
    public string? TargetAuthorNumber { get; set; }
    public string? TargetAuthorUuid { get; set; }
    public long TargetSentTimestamp { get; set; }
    public bool IsRemove { get; set; }
}

/// <summary>A receipt; exactly one of the <c>Is…</c> flags is set.</summary>
internal sealed class ReceiptMessageDto
{
    public long When { get; set; }
    public bool IsDelivery { get; set; }
    public bool IsRead { get; set; }
    public bool IsViewed { get; set; }
    public List<long>? Timestamps { get; set; }
}

/// <summary>A typing indicator; <see cref="Action"/> is <c>STARTED</c> or <c>STOPPED</c>.</summary>
internal sealed class TypingMessageDto
{
    public string? Action { get; set; }
    public long Timestamp { get; set; }
    public string? GroupId { get; set; }
}
