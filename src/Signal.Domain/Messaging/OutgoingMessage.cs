using System.Text;
using Signal.Domain.Exceptions;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Messaging;

/// <summary>
/// An immutable, validated message to send. Build it with <see cref="Create"/> or <see cref="To"/>
/// and the fluent <see cref="OutgoingMessageBuilder"/>.
/// </summary>
/// <example>
/// <code>
/// var message = OutgoingMessage.To(PhoneNumber.Parse("+4915112345678"))
///     .WithStyledText("**Hello**")
///     .WithAttachment(bytes, "image/png", "chart.png")
///     .Build();
/// </code>
/// </example>
public sealed class OutgoingMessage
{
    internal OutgoingMessage(OutgoingMessageBuilder builder)
    {
        Recipients = [.. builder.RecipientList];
        Text = builder.TextValue;
        TextMode = builder.TextModeValue;
        Attachments = [.. builder.AttachmentList];
        Mentions = [.. builder.MentionList];
        Quote = builder.QuoteValue;
        EditTimestamp = builder.EditTimestampValue;
        ViewOnce = builder.ViewOnceValue;
        NotifySelf = builder.NotifySelfValue;
    }

    /// <summary>The recipients (at least one, without duplicates).</summary>
    public IReadOnlyList<Recipient> Recipients { get; }

    /// <summary>The message body, if any.</summary>
    public string? Text { get; }

    /// <summary>How <see cref="Text"/> is interpreted.</summary>
    public TextMode TextMode { get; }

    /// <summary>Attachments as base64 or data URIs (<c>data:&lt;mime&gt;;filename=&lt;name&gt;;base64,&lt;data&gt;</c>).</summary>
    public IReadOnlyList<string> Attachments { get; }

    /// <summary>Mentions inside <see cref="Text"/>.</summary>
    public IReadOnlyList<Mention> Mentions { get; }

    /// <summary>The message being replied to, if any.</summary>
    public Quote? Quote { get; }

    /// <summary>Timestamp of a previously sent message to edit; <see langword="null"/> for a new message.</summary>
    public long? EditTimestamp { get; }

    /// <summary>Whether the message can be viewed only once.</summary>
    public bool ViewOnce { get; }

    /// <summary>Whether the sending account's other devices should notify; <see langword="null"/> uses the API default.</summary>
    public bool? NotifySelf { get; }

    /// <summary>Starts building a message without recipients.</summary>
    /// <returns>A new builder.</returns>
    public static OutgoingMessageBuilder Create() => new();

    /// <summary>Starts building a message to the given recipients.</summary>
    /// <param name="recipients">The recipients. Phone numbers and group ids convert implicitly.</param>
    /// <returns>A new builder.</returns>
    public static OutgoingMessageBuilder To(params IEnumerable<Recipient> recipients) => new OutgoingMessageBuilder().To(recipients);
}

/// <summary>Fluent builder for <see cref="OutgoingMessage"/>. Validation happens in <see cref="Build"/>.</summary>
public sealed class OutgoingMessageBuilder
{
    internal List<Recipient> RecipientList { get; } = [];
    internal List<string> AttachmentList { get; } = [];
    internal List<Mention> MentionList { get; } = [];
    internal string? TextValue { get; private set; }
    internal TextMode TextModeValue { get; private set; }
    internal Quote? QuoteValue { get; private set; }
    internal long? EditTimestampValue { get; private set; }
    internal bool ViewOnceValue { get; private set; }
    internal bool? NotifySelfValue { get; private set; }

    /// <summary>Adds recipients. Duplicates are ignored. Phone numbers, account ids, usernames and group ids convert implicitly.</summary>
    /// <param name="recipients">The recipients to add.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A recipient is <see langword="default"/> (holds no value).</exception>
    public OutgoingMessageBuilder To(params IEnumerable<Recipient> recipients)
    {
        foreach (var recipient in recipients)
        {
            if (recipient.Value is null)
            {
                throw new ArgumentException("A default (empty) recipient cannot be addressed.", nameof(recipients));
            }

            if (!RecipientList.Contains(recipient))
            {
                RecipientList.Add(recipient);
            }
        }

        return this;
    }

    /// <summary>Sets the message text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="mode">How the text is interpreted.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder WithText(string? text, TextMode mode = TextMode.Normal)
    {
        TextValue = text;
        TextModeValue = mode;
        return this;
    }

    /// <summary>Sets a text using Signal's markdown-like styling (<see cref="TextMode.Styled"/>).</summary>
    /// <param name="text">The styled text, e.g. <c>**bold** and *italic*</c>.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder WithStyledText(string text) => WithText(text, TextMode.Styled);

    /// <summary>Adds an attachment, encoding it as a data URI.</summary>
    /// <param name="content">The file content.</param>
    /// <param name="contentType">The MIME type, e.g. <c>image/png</c>.</param>
    /// <param name="fileName">Optional file name shown to recipients.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder WithAttachment(ReadOnlySpan<byte> content, string contentType, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var builder = new StringBuilder("data:").Append(contentType);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            builder.Append(";filename=").Append(fileName);
        }

        AttachmentList.Add(builder.Append(";base64,").Append(Convert.ToBase64String(content)).ToString());
        return this;
    }

    /// <summary>Adds an attachment that is already base64 encoded (optionally as data URI).</summary>
    /// <param name="base64OrDataUri">Plain base64, or <c>data:&lt;mime&gt;;filename=&lt;name&gt;;base64,&lt;data&gt;</c>.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder WithBase64Attachment(string base64OrDataUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64OrDataUri);
        AttachmentList.Add(base64OrDataUri);
        return this;
    }

    /// <summary>Mentions a user at a position in the text.</summary>
    /// <param name="author">Phone number or UUID of the mentioned user.</param>
    /// <param name="start">Start position in the text (UTF-16 code units).</param>
    /// <param name="length">Length of the mentioned span.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="SignalDomainException"><paramref name="start"/> is negative or <paramref name="length"/> is not positive.</exception>
    public OutgoingMessageBuilder WithMention(string author, int start, int length)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        if (start < 0 || length <= 0)
        {
            throw new SignalDomainException("Mention start must be >= 0 and length > 0.");
        }

        MentionList.Add(new Mention(author, start, length));
        return this;
    }

    /// <summary>Replies to (quotes) a message.</summary>
    /// <param name="timestamp">Timestamp of the quoted message.</param>
    /// <param name="author">Phone number or UUID of the quoted message's author.</param>
    /// <param name="text">Text of the quoted message, shown in the preview.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder Quoting(long timestamp, string author, string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        QuoteValue = new Quote(timestamp, author, text);
        return this;
    }

    /// <summary>Turns the send into an edit of a message sent earlier by the same account.</summary>
    /// <param name="timestamp">Timestamp of the message to edit.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder Editing(long timestamp)
    {
        EditTimestampValue = timestamp;
        return this;
    }

    /// <summary>Marks the message as view-once.</summary>
    /// <param name="viewOnce">Whether the message is view-once.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder AsViewOnce(bool viewOnce = true)
    {
        ViewOnceValue = viewOnce;
        return this;
    }

    /// <summary>Controls whether the sending account's other devices are notified.</summary>
    /// <param name="notify">Whether to notify the own devices.</param>
    /// <returns>This builder.</returns>
    public OutgoingMessageBuilder NotifySelf(bool notify = true)
    {
        NotifySelfValue = notify;
        return this;
    }

    /// <summary>Validates the state and creates the immutable message.</summary>
    /// <returns>The message.</returns>
    /// <exception cref="SignalDomainException">
    /// There is no recipient, neither text nor an attachment, or a mention exceeds the text.
    /// </exception>
    public OutgoingMessage Build()
    {
        if (RecipientList.Count == 0)
        {
            throw new SignalDomainException("A message needs at least one recipient.");
        }

        if (string.IsNullOrEmpty(TextValue) && AttachmentList.Count == 0)
        {
            throw new SignalDomainException("A message needs a text or at least one attachment.");
        }

        if (MentionList.Any(m => m.Start + m.Length > (TextValue?.Length ?? 0)))
        {
            throw new SignalDomainException("A mention exceeds the message text.");
        }

        return new OutgoingMessage(this);
    }
}
