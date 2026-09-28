namespace Signal.Domain.Messaging;

/// <summary>Metadata of a received attachment. Download the content through the attachment service by <see cref="Id"/>.</summary>
/// <param name="Id">The attachment id assigned by signal-cli (used with <c>GET /v1/attachments/{id}</c>).</param>
/// <param name="ContentType">MIME type, e.g. <c>image/png</c>.</param>
/// <param name="Filename">Original file name, if the sender provided one.</param>
/// <param name="Size">Size in bytes, if known.</param>
public sealed record Attachment(string Id, string? ContentType, string? Filename, long? Size);

/// <summary>A mention of a user inside a message body.</summary>
/// <param name="Author">Phone number or UUID of the mentioned user.</param>
/// <param name="Start">Start position of the mention in the text (UTF-16 code units).</param>
/// <param name="Length">Length of the mention placeholder in the text.</param>
/// <param name="Name">Display name of the mentioned user (received mentions only).</param>
public sealed record Mention(string Author, int Start, int Length, string? Name = null);

/// <summary>A quoted (replied-to) message.</summary>
/// <param name="Timestamp">Timestamp of the quoted message; identifies it together with <paramref name="Author"/>.</param>
/// <param name="Author">Phone number or UUID of the quoted message's author.</param>
/// <param name="Text">Text of the quoted message, shown in the reply preview.</param>
public sealed record Quote(long Timestamp, string Author, string? Text);

/// <summary>An emoji reaction to a message, or the removal of one.</summary>
/// <param name="Emoji">The reaction emoji.</param>
/// <param name="TargetAuthor">Phone number or UUID of the author of the message reacted to.</param>
/// <param name="TargetTimestamp">Timestamp of the message reacted to.</param>
/// <param name="IsRemove"><see langword="true"/> if the reaction was removed.</param>
public sealed record Reaction(string Emoji, string TargetAuthor, long TargetTimestamp, bool IsRemove);

/// <summary>How the text of an outgoing message is interpreted.</summary>
public enum TextMode
{
    /// <summary>Plain text.</summary>
    Normal,

    /// <summary>Enables markdown-like styling (<c>**bold**</c>, <c>*italic*</c>, <c>~strike~</c>, <c>`mono`</c>, <c>||spoiler||</c>).</summary>
    Styled,
}

/// <summary>The kind of a receipt.</summary>
public enum ReceiptType
{
    /// <summary>The message was delivered to a device. Sent automatically by Signal.</summary>
    Delivery,

    /// <summary>The message was read.</summary>
    Read,

    /// <summary>A view-once or media message was viewed.</summary>
    Viewed,
}

/// <summary>State of a typing indicator.</summary>
public enum TypingAction
{
    /// <summary>The user started typing.</summary>
    Started,

    /// <summary>The user stopped typing.</summary>
    Stopped,
}
