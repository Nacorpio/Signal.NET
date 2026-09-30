using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Http.Dtos;

namespace Signal.Infrastructure.Mapping;

/// <summary>
/// Anti-corruption layer: translates signal-cli envelope JSON into the domain model. All knowledge about the
/// quirks of the wire format (legacy <c>source</c> field, internal group ids, <c>UPDATE</c> group messages,
/// receipt flags) is contained here.
/// </summary>
internal static class EnvelopeMapper
{
    /// <summary>Maps one received item.</summary>
    /// <param name="dto">The deserialized item.</param>
    /// <param name="fallbackAccount">Used when the item does not state its account.</param>
    /// <param name="includeStories">Whether story messages are mapped (<c>Receive:IgnoreStories = false</c>).</param>
    /// <returns>
    /// The envelope, or <see langword="null"/> when it has no identifiable sender or no supported content
    /// (e.g. read or blocked-list sync messages, ICE updates, or stories when <paramref name="includeStories"/> is off).
    /// </returns>
    public static IncomingEnvelope? Map(ReceivedMessageDto dto, PhoneNumber fallbackAccount, bool includeStories = false)
    {
        if (dto.Envelope is not { } envelope)
        {
            return null;
        }

        var account = PhoneNumber.TryParse(dto.Account, out var parsedAccount) ? parsedAccount : fallbackAccount;

        // Newer signal-cli versions fill sourceNumber/sourceUuid; older ones only the ambiguous "source".
        PhoneNumber? number = PhoneNumber.TryParse(envelope.SourceNumber, out var n) ? n
            : PhoneNumber.TryParse(envelope.Source, out n) ? n
            : null;
        Guid? uuid = Guid.TryParse(envelope.SourceUuid, out var u) ? u
            : Guid.TryParse(envelope.Source, out u) ? u
            : null;
        if (number is null && uuid is null)
        {
            return null;
        }

        // signal-cli sets exactly one content property; envelopes without supported content are dropped.
        EnvelopeContent content;
        if (Map(envelope.DataMessage) is { } data)
        {
            content = data;
        }
        else if (envelope.EditMessage is { TargetSentTimestamp: > 0 } edit && Map(edit.DataMessage) is { } edited)
        {
            content = new EditMessage(edit.TargetSentTimestamp, edited);
        }
        else if (Map(envelope.SyncMessage?.SentMessage) is { } transcript)
        {
            content = transcript;
        }
        else if (envelope.StoryMessage is { } storyDto)
        {
            // In WebSocket modes the API can't be asked to skip stories, so the option is also enforced here.
            if (!includeStories || Map(storyDto) is not { } story)
            {
                return null;
            }

            content = story;
        }
        else if (Map(envelope.CallMessage) is { } call)
        {
            content = call;
        }
        else if (Map(envelope.ReceiptMessage) is { } receipt)
        {
            content = receipt;
        }
        else if (Map(envelope.TypingMessage) is { } typing)
        {
            content = typing;
        }
        else
        {
            return null;
        }

        return new IncomingEnvelope(account, new Sender(number, uuid, envelope.SourceName, envelope.SourceDevice), envelope.Timestamp, content);
    }

    /// <summary>Maps a data message; prefers phone numbers over UUIDs for authors of quotes, reactions and mentions.</summary>
    private static DataMessage? Map(DataMessageDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new DataMessage(dto.Timestamp, dto.Message)
        {
            Group = ToGroupId(dto.GroupInfo?.GroupId),
            IsGroupUpdate = string.Equals(dto.GroupInfo?.Type, "UPDATE", StringComparison.OrdinalIgnoreCase),
            ViewOnce = dto.ViewOnce,
            Attachments = [.. (dto.Attachments ?? []).Where(a => a.Id is not null).Select(a => new Attachment(a.Id!, a.ContentType, a.Filename, a.Size))],
            Mentions = [.. (dto.Mentions ?? [])
                .Where(m => (m.Number ?? m.Uuid) is not null)
                .Select(m => new Mention((m.Number ?? m.Uuid)!, m.Start, m.Length, m.Name))],
            Quote = dto.Quote is { } q && (q.AuthorNumber ?? q.AuthorUuid ?? q.Author) is { } author
                ? new Quote(q.Id, author, q.Text)
                : null,
            Reaction = dto.Reaction is { Emoji: { } emoji } r && (r.TargetAuthorNumber ?? r.TargetAuthorUuid ?? r.TargetAuthor) is { } target
                ? new Reaction(emoji, target, r.TargetSentTimestamp, r.IsRemove)
                : null,

            // A malformed sticker is dropped rather than failing the whole message.
            Sticker = dto.Sticker is { PackId: { } packId, StickerId: >= 0 } s && Sticker.TryParse($"{packId}:{s.StickerId}", out var sticker)
                ? sticker
                : null,
            RemoteDelete = dto.RemoteDelete is { Timestamp: > 0 } d ? new RemoteDelete(d.Timestamp) : null,
            TextStyles = [.. (dto.TextStyles ?? [])
                .Where(t => t is { Start: >= 0, Length: > 0 })
                .Select(t => ToTextStyle(t.Style) is { } style ? new StyledRange(style, t.Start, t.Length) : null)
                .OfType<StyledRange>()],
        };
    }

    /// <summary>Maps signal-cli's style names; <c>NONE</c> and unknown styles are skipped.</summary>
    private static TextStyle? ToTextStyle(string? style) => style?.ToUpperInvariant() switch
    {
        "BOLD" => TextStyle.Bold,
        "ITALIC" => TextStyle.Italic,
        "SPOILER" => TextStyle.Spoiler,
        "STRIKETHROUGH" => TextStyle.Strikethrough,
        "MONOSPACE" => TextStyle.Monospace,
        _ => null,
    };

    /// <summary>
    /// Maps a message sent from another device. An edit takes precedence over the unwrapped data fields. The
    /// conversation is the message's group, otherwise the destination (phone number preferred over UUID).
    /// </summary>
    private static SentTranscript? Map(SyncSentMessageDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        long? editTarget = null;
        DataMessage? message;
        if (dto.EditMessage is { TargetSentTimestamp: > 0 } edit)
        {
            message = Map(edit.DataMessage);
            editTarget = edit.TargetSentTimestamp;
        }
        else
        {
            // Without an unwrapped message the timestamp is missing (0).
            message = dto.Timestamp > 0 ? Map((DataMessageDto)dto) : null;
        }

        Recipient? conversation = message?.Group is { } group ? group
            : PhoneNumber.TryParse(dto.DestinationNumber, out var number) ? number
            : PhoneNumber.TryParse(dto.Destination, out number) ? number
            : AccountId.TryParse(dto.DestinationUuid, out var id) ? id
            : AccountId.TryParse(dto.Destination, out id) ? id
            : null;

        return message is not null && conversation is { } target
            ? new SentTranscript(target, message) { EditTargetTimestamp = editTarget }
            : null;
    }

    /// <summary>Maps a story; stories with neither a file nor text are dropped.</summary>
    private static StoryMessage? Map(StoryMessageDto dto)
    {
        var file = dto.FileAttachment is { Id: { } id } f ? new Attachment(id, f.ContentType, f.Filename, f.Size) : null;
        var text = dto.TextAttachment?.Text;
        return file is null && string.IsNullOrEmpty(text)
            ? null
            : new StoryMessage(dto.AllowsReplies, ToGroupId(dto.GroupId)) { File = file, Text = text };
    }

    /// <summary>Maps a call event; envelopes with only ICE updates (or nothing) are dropped.</summary>
    private static CallMessage? Map(CallMessageDto? dto) => dto switch
    {
        { OfferMessage: { } offer } => new CallMessage(CallEventKind.Offer, offer.Id)
        {
            IsVideo = string.Equals(offer.Type, "VIDEO_CALL", StringComparison.OrdinalIgnoreCase),
        },
        { AnswerMessage: { } answer } => new CallMessage(CallEventKind.Answer, answer.Id),
        { BusyMessage: { } busy } => new CallMessage(CallEventKind.Busy, busy.Id),
        { HangupMessage: { } hangup } => new CallMessage(CallEventKind.Hangup, hangup.Id),
        _ => null,
    };

    /// <summary>Maps a receipt; viewed takes precedence over read over delivery.</summary>
    private static ReceiptMessage? Map(ReceiptMessageDto? dto) => dto is null
        ? null
        : new ReceiptMessage(
            dto.IsViewed ? ReceiptType.Viewed : dto.IsRead ? ReceiptType.Read : ReceiptType.Delivery,
            dto.When,
            dto.Timestamps ?? []);

    /// <summary>Maps a typing indicator; anything but <c>STARTED</c> means stopped.</summary>
    private static TypingMessage? Map(TypingMessageDto? dto) => dto is null
        ? null
        : new TypingMessage(
            string.Equals(dto.Action, "STARTED", StringComparison.OrdinalIgnoreCase) ? TypingAction.Started : TypingAction.Stopped,
            dto.Timestamp,
            ToGroupId(dto.GroupId));

    /// <summary>Envelopes carry the internal id; the REST API expects <c>group.base64(internal_id)</c>.</summary>
    private static GroupId? ToGroupId(string? internalId) =>
        string.IsNullOrWhiteSpace(internalId) ? null : GroupId.FromInternalId(internalId);
}
