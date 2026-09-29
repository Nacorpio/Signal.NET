namespace Signal.Infrastructure.Http.Dtos;

// Data transfer objects mirroring the signal-cli-rest-api Swagger schema
// (https://bbernhard.github.io/signal-cli-rest-api/). Property names are mapped to snake_case by
// SignalRestJsonContext, e.g. Base64Attachments <-> "base64_attachments". These types never leave the
// infrastructure layer; adapters translate them to and from domain objects.

/// <summary>Error body returned by the API on failures: <c>{"error": "..."}</c>.</summary>
internal sealed class ErrorResponseDto
{
    public string? Error { get; set; }
}

/// <summary>Response of <c>GET /v1/about</c>.</summary>
internal sealed class AboutDto
{
    public List<string>? Versions { get; set; }
    public int Build { get; set; }
    public string? Mode { get; set; }
    public string? Version { get; set; }
    public Dictionary<string, List<string>>? Capabilities { get; set; }
}

/// <summary>Request body of <c>POST /v2/send</c>.</summary>
internal sealed class SendMessageRequestDto
{
    public string? Message { get; set; }

    /// <summary>The sending account.</summary>
    public required string Number { get; set; }

    /// <summary>Phone numbers, UUIDs, usernames or <c>group.…</c> ids.</summary>
    public required List<string> Recipients { get; set; }

    /// <summary>Base64 strings or data URIs.</summary>
    public List<string>? Base64Attachments { get; set; }

    /// <summary><c>normal</c> or <c>styled</c>; omitted means normal.</summary>
    public string? TextMode { get; set; }

    public List<MentionDto>? Mentions { get; set; }
    public long? QuoteTimestamp { get; set; }
    public string? QuoteAuthor { get; set; }
    public string? QuoteMessage { get; set; }
    public long? EditTimestamp { get; set; }
    public bool? ViewOnce { get; set; }
    public bool? NotifySelf { get; set; }
}

/// <summary>A mention inside a send request.</summary>
internal sealed class MentionDto
{
    public required string Author { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
}

/// <summary>
/// Response of <c>POST /v2/send</c> and <c>DELETE /v1/remote-delete/{number}</c>. The timestamp is sent as a JSON string.
/// </summary>
internal sealed class SendMessageResponseDto
{
    public long Timestamp { get; set; }
}

/// <summary>Body of <c>POST /v1/accounts/{number}/username</c>.</summary>
internal sealed class SetUsernameRequestDto
{
    public required string Username { get; set; }
}

/// <summary>Response of <c>POST /v1/accounts/{number}/username</c> (201; a 204 has no body).</summary>
internal sealed class SetUsernameResponseDto
{
    public string? Username { get; set; }
    public string? UsernameLink { get; set; }
}

/// <summary>Body of <c>PUT /v1/accounts/{number}/settings</c>; null fields are omitted and left unchanged.</summary>
internal sealed class UpdateAccountSettingsRequestDto
{
    public bool? DiscoverableByNumber { get; set; }
    public bool? ShareNumber { get; set; }
}

/// <summary>Body of <c>POST /v1/accounts/{number}/pin</c>.</summary>
internal sealed class SetPinRequestDto
{
    public required string Pin { get; set; }
}

/// <summary>Body of <c>POST /v1/accounts/{number}/rate-limit-challenge</c>.</summary>
internal sealed class RateLimitChallengeRequestDto
{
    public required string ChallengeToken { get; set; }
    public required string Captcha { get; set; }
}

/// <summary>Response of <c>GET /v1/qrcodelink/raw</c>.</summary>
internal sealed class DeviceLinkUriResponseDto
{
    public string? DeviceLinkUri { get; set; }
}

/// <summary>A device as returned by <c>GET /v1/devices/{number}</c>; timestamps are Unix milliseconds.</summary>
internal sealed class DeviceDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public long? CreationTimestamp { get; set; }
    public long? LastSeenTimestamp { get; set; }
}

/// <summary>Body of <c>POST /v1/devices/{number}</c>.</summary>
internal sealed class AddDeviceRequestDto
{
    public required string Uri { get; set; }
}

/// <summary>Body of <c>POST /v1/register/{number}</c>.</summary>
internal sealed class RegisterNumberRequestDto
{
    public string? Captcha { get; set; }
    public bool? UseVoice { get; set; }
}

/// <summary>Body of <c>POST /v1/register/{number}/verify/{token}</c>.</summary>
internal sealed class VerifyNumberRequestDto
{
    public string? Pin { get; set; }
}

/// <summary>Body of <c>POST /v1/unregister/{number}</c>.</summary>
internal sealed class UnregisterNumberRequestDto
{
    public bool DeleteAccount { get; set; }
    public bool DeleteLocalData { get; set; }
}

/// <summary>Body of <c>DELETE /v1/remote-delete/{number}</c>.</summary>
internal sealed class RemoteDeleteRequestDto
{
    /// <summary>The conversation of the message: phone number, UUID, username or <c>group.…</c> id.</summary>
    public required string Recipient { get; set; }

    /// <summary>Timestamp of the message to delete.</summary>
    public long Timestamp { get; set; }
}

/// <summary>Body of <c>POST</c>/<c>DELETE /v1/reactions/{number}</c>.</summary>
internal sealed class ReactionRequestDto
{
    public required string Recipient { get; set; }
    public required string Reaction { get; set; }
    public required string TargetAuthor { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>Body of <c>POST /v1/receipts/{number}</c>; <see cref="ReceiptType"/> is <c>read</c> or <c>viewed</c>.</summary>
internal sealed class ReceiptRequestDto
{
    public required string Recipient { get; set; }
    public required string ReceiptType { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>Body of <c>PUT</c>/<c>DELETE /v1/typing-indicator/{number}</c>.</summary>
internal sealed class TypingIndicatorRequestDto
{
    public required string Recipient { get; set; }
}

/// <summary>A group as returned by <c>GET /v1/groups/{number}[/{groupid}]</c>.</summary>
internal sealed class GroupDto
{
    /// <summary>The REST form (<c>group.…</c>).</summary>
    public string? Id { get; set; }

    /// <summary>The signal-cli internal id.</summary>
    public string? InternalId { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Members { get; set; }
    public List<string>? Admins { get; set; }
    public List<string>? PendingInvites { get; set; }
    public List<string>? PendingRequests { get; set; }
    public bool Blocked { get; set; }
    public string? InviteLink { get; set; }
}

/// <summary>Body of <c>POST /v1/groups/{number}</c>.</summary>
internal sealed class CreateGroupRequestDto
{
    public required string Name { get; set; }
    public required List<string> Members { get; set; }
    public string? Description { get; set; }
}

/// <summary>Response of <c>POST /v1/groups/{number}</c>.</summary>
internal sealed class CreateGroupResponseDto
{
    public string? Id { get; set; }
}

/// <summary>Body of <c>PUT /v1/groups/{number}/{groupid}</c>; null fields are omitted and left unchanged.</summary>
internal sealed class UpdateGroupRequestDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Base64Avatar { get; set; }
}

/// <summary>Body of <c>POST</c>/<c>DELETE /v1/groups/{number}/{groupid}/members</c>.</summary>
internal sealed class GroupMembersRequestDto
{
    public required List<string> Members { get; set; }
}

/// <summary>Body of <c>POST</c>/<c>DELETE /v1/groups/{number}/{groupid}/admins</c>.</summary>
internal sealed class GroupAdminsRequestDto
{
    public required List<string> Admins { get; set; }
}

/// <summary>A contact as returned by <c>GET /v1/contacts/{number}</c>.</summary>
internal sealed class ContactDto
{
    public string? Number { get; set; }
    public string? Uuid { get; set; }
    public string? Name { get; set; }
    public string? ProfileName { get; set; }
    public string? Username { get; set; }
    public bool Blocked { get; set; }
}

/// <summary>Body of <c>PUT /v1/contacts/{number}</c>.</summary>
internal sealed class UpdateContactRequestDto
{
    public required string Recipient { get; set; }
    public string? Name { get; set; }
    public int? ExpirationInSeconds { get; set; }
}

/// <summary>An identity as returned by <c>GET /v1/identities/{number}</c>.</summary>
internal sealed class IdentityDto
{
    public string? Number { get; set; }
    public string? Uuid { get; set; }
    public string? Status { get; set; }
    public string? Fingerprint { get; set; }
    public string? SafetyNumber { get; set; }
    public string? Added { get; set; }
}

/// <summary>Body of <c>PUT /v1/identities/{number}/trust/{numberToTrust}</c>.</summary>
internal sealed class TrustIdentityRequestDto
{
    public bool? TrustAllKnownKeys { get; set; }
    public string? VerifiedSafetyNumber { get; set; }
}

/// <summary>Body of <c>PUT /v1/profiles/{number}</c>.</summary>
internal sealed class UpdateProfileRequestDto
{
    public required string Name { get; set; }
    public string? About { get; set; }
    public string? Base64Avatar { get; set; }
}
