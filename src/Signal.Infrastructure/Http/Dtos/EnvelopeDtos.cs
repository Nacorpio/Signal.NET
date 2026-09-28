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
    public ReceiptMessageDto? ReceiptMessage { get; set; }
    public TypingMessageDto? TypingMessage { get; set; }
}

/// <summary>A regular message.</summary>
internal sealed class DataMessageDto
{
    public long Timestamp { get; set; }
    public string? Message { get; set; }
    public bool ViewOnce { get; set; }
    public GroupInfoDto? GroupInfo { get; set; }
    public List<AttachmentDto>? Attachments { get; set; }
    public List<IncomingMentionDto>? Mentions { get; set; }
    public QuoteDto? Quote { get; set; }
    public ReactionDto? Reaction { get; set; }
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
