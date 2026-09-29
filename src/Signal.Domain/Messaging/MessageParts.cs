using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Signal.Domain.Exceptions;

namespace Signal.Domain.Messaging;

/// <summary>
/// A sticker from an installed sticker pack, written as <c>packId:stickerId</c> (e.g. <c>f3a9…:4</c>).
/// Implements <see cref="IParsable{TSelf}"/>, so command arguments can bind to it.
/// </summary>
public readonly record struct Sticker : IParsable<Sticker>
{
    /// <summary>Creates a sticker reference.</summary>
    /// <param name="packId">The hex id of the sticker pack; normalized to lower case.</param>
    /// <param name="stickerId">The index of the sticker within the pack.</param>
    /// <exception cref="SignalDomainException"><paramref name="packId"/> is not hex, or <paramref name="stickerId"/> is negative.</exception>
    public Sticker(string packId, int stickerId)
    {
        var trimmed = packId?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.All(char.IsAsciiHexDigit))
        {
            throw new SignalDomainException($"'{packId}' is not a valid sticker pack id (expected hex).");
        }

        if (stickerId < 0)
        {
            throw new SignalDomainException("The sticker id must not be negative.");
        }

        PackId = trimmed.ToLowerInvariant();
        StickerId = stickerId;
    }

    /// <summary>The hex id of the sticker pack.</summary>
    public string PackId { get; }

    /// <summary>The index of the sticker within the pack.</summary>
    public int StickerId { get; }

    /// <summary>Parses <c>packId:stickerId</c> (surrounding whitespace is trimmed).</summary>
    /// <param name="value">The sticker text.</param>
    /// <returns>The sticker.</returns>
    /// <exception cref="SignalDomainException"><paramref name="value"/> is not in the form <c>packId:stickerId</c>.</exception>
    public static Sticker Parse(string value) =>
        TryParse(value, out var sticker)
            ? sticker
            : throw new SignalDomainException($"'{value}' is not a sticker (expected packId:stickerId).");

    /// <summary>Tries to parse <c>packId:stickerId</c>.</summary>
    /// <param name="value">The sticker text.</param>
    /// <param name="sticker">The sticker when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid sticker.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out Sticker sticker)
    {
        sticker = default;
        var parts = value?.Trim().Split(':');
        if (parts is not [var packId, var id]
            || packId.Length == 0
            || !packId.All(char.IsAsciiHexDigit)
            || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var stickerId))
        {
            return false;
        }

        sticker = new Sticker(packId, stickerId);
        return true;
    }

    static Sticker IParsable<Sticker>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<Sticker>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Sticker result) =>
        TryParse(s, out result);

    /// <summary>Returns the API form <c>packId:stickerId</c>.</summary>
    /// <returns>The sticker text, or an empty string for <see langword="default"/>.</returns>
    public override string ToString() => PackId is null ? string.Empty : $"{PackId}:{StickerId}";
}

/// <summary>
/// A link preview card shown above the message text. Signal clients only render it if <see cref="Url"/> appears in
/// the text, which <see cref="OutgoingMessageBuilder.Build"/> enforces.
/// </summary>
/// <param name="Url">The previewed <c>http</c> or <c>https</c> URL.</param>
/// <param name="Title">The card title.</param>
/// <param name="Description">The card description.</param>
/// <param name="Base64Thumbnail">The thumbnail image as base64 or data URI.</param>
public sealed record LinkPreview(string Url, string? Title = null, string? Description = null, string? Base64Thumbnail = null);

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
