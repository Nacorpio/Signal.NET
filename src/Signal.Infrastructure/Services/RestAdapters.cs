using System.Net;
using Signal.Application.Abstractions;
using Signal.Domain.Entities;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;
using Signal.Infrastructure.Http;
using Signal.Infrastructure.Http.Dtos;

namespace Signal.Infrastructure.Services;

// Adapters implementing the application ports on top of the REST API. Each adapter is a thin translation
// between domain types and DTOs; HTTP concerns (errors, resilience, serialization) live in SignalApiClient.
// All adapters are transient because they wrap the typed HttpClient managed by IHttpClientFactory.

/// <summary>
/// Implements <see cref="IMessageSender"/> with <c>POST /v2/send</c> (never retried by the resilience pipeline, so a
/// message is not sent twice) and <c>DELETE /v1/remote-delete/{number}</c> (retried like any DELETE; deleting the same
/// message twice is harmless).
/// </summary>
internal sealed class RestMessageSender(SignalApiClient api) : IMessageSender
{
    public async Task<SendResult> SendAsync(PhoneNumber account, OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequestDto
        {
            Number = account.Value,
            Recipients = [.. message.Recipients.Select(r => r.Address)],
            Message = message.Text,
            TextMode = message.TextMode == TextMode.Styled ? "styled" : null,
            Base64Attachments = message.Attachments.Count > 0 ? [.. message.Attachments] : null,
            Mentions = message.Mentions.Count > 0
                ? [.. message.Mentions.Select(m => new MentionDto { Author = m.Author, Start = m.Start, Length = m.Length })]
                : null,
            QuoteTimestamp = message.Quote?.Timestamp,
            QuoteAuthor = message.Quote?.Author,
            QuoteMessage = message.Quote?.Text,
            EditTimestamp = message.EditTimestamp,
            ViewOnce = message.ViewOnce ? true : null,
            NotifySelf = message.NotifySelf,
        };

        var response = await api.SendAsync(HttpMethod.Post, "v2/send", request,
            SignalRestJsonContext.Default.SendMessageRequestDto, SignalRestJsonContext.Default.SendMessageResponseDto, cancellationToken);
        return new SendResult(response.Timestamp);
    }

    public async Task<SendResult> RemoteDeleteAsync(PhoneNumber account, Recipient recipient, long targetTimestamp, CancellationToken cancellationToken = default)
    {
        var response = await api.SendAsync(HttpMethod.Delete, $"v1/remote-delete/{SignalApiClient.Escape(account.Value)}",
            new RemoteDeleteRequestDto { Recipient = recipient.Address, Timestamp = targetTimestamp },
            SignalRestJsonContext.Default.RemoteDeleteRequestDto, SignalRestJsonContext.Default.SendMessageResponseDto, cancellationToken);
        return new SendResult(response.Timestamp);
    }
}

/// <summary>Implements <see cref="IReactionService"/> with <c>POST</c>/<c>DELETE /v1/reactions/{number}</c>.</summary>
internal sealed class RestReactionService(SignalApiClient api) : IReactionService
{
    public Task SendReactionAsync(PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, account, recipient, emoji, targetAuthor, targetTimestamp, cancellationToken);

    public Task RemoveReactionAsync(PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, account, recipient, emoji, targetAuthor, targetTimestamp, cancellationToken);

    private Task SendAsync(HttpMethod method, PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long timestamp, CancellationToken cancellationToken) =>
        api.SendAsync(method, $"v1/reactions/{SignalApiClient.Escape(account.Value)}",
            new ReactionRequestDto { Recipient = recipient.Address, Reaction = emoji, TargetAuthor = targetAuthor, Timestamp = timestamp },
            SignalRestJsonContext.Default.ReactionRequestDto, cancellationToken);
}

/// <summary>Implements <see cref="IReceiptService"/> with <c>POST /v1/receipts/{number}</c>.</summary>
internal sealed class RestReceiptService(SignalApiClient api) : IReceiptService
{
    public Task SendReceiptAsync(PhoneNumber account, Recipient recipient, long timestamp, ReceiptType type = ReceiptType.Read, CancellationToken cancellationToken = default)
    {
        if (type == ReceiptType.Delivery)
        {
            throw new ArgumentException("Delivery receipts are sent automatically and cannot be sent manually.", nameof(type));
        }

        return api.SendAsync(HttpMethod.Post, $"v1/receipts/{SignalApiClient.Escape(account.Value)}",
            new ReceiptRequestDto { Recipient = recipient.Address, ReceiptType = type == ReceiptType.Viewed ? "viewed" : "read", Timestamp = timestamp },
            SignalRestJsonContext.Default.ReceiptRequestDto, cancellationToken);
    }
}

/// <summary>Implements <see cref="ITypingIndicatorService"/> with <c>PUT</c>/<c>DELETE /v1/typing-indicator/{number}</c>.</summary>
internal sealed class RestTypingIndicatorService(SignalApiClient api) : ITypingIndicatorService
{
    public Task StartTypingAsync(PhoneNumber account, Recipient recipient, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, account, recipient, cancellationToken);

    public Task StopTypingAsync(PhoneNumber account, Recipient recipient, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, account, recipient, cancellationToken);

    private Task SendAsync(HttpMethod method, PhoneNumber account, Recipient recipient, CancellationToken cancellationToken) =>
        api.SendAsync(method, $"v1/typing-indicator/{SignalApiClient.Escape(account.Value)}",
            new TypingIndicatorRequestDto { Recipient = recipient.Address }, SignalRestJsonContext.Default.TypingIndicatorRequestDto, cancellationToken);
}

/// <summary>Implements <see cref="IGroupService"/> with the <c>/v1/groups/{number}</c> endpoints; maps <see cref="GroupDto"/> to <see cref="Group"/>.</summary>
internal sealed class RestGroupService(SignalApiClient api) : IGroupService
{
    public async Task<IReadOnlyList<Group>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default)
    {
        var groups = await api.GetAsync(GroupsPath(account), SignalRestJsonContext.Default.ListGroupDto, cancellationToken);
        return [.. groups.Select(ToEntity).OfType<Group>()];
    }

    public async Task<Group?> GetAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default)
    {
        try
        {
            return ToEntity(await api.GetAsync(GroupPath(account, group), SignalRestJsonContext.Default.GroupDto, cancellationToken));
        }
        catch (SignalApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
    }

    public async Task<GroupId> CreateAsync(PhoneNumber account, string name, IEnumerable<string> members, string? description = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var response = await api.SendAsync(HttpMethod.Post, GroupsPath(account),
            new CreateGroupRequestDto { Name = name, Members = [.. members], Description = description },
            SignalRestJsonContext.Default.CreateGroupRequestDto, SignalRestJsonContext.Default.CreateGroupResponseDto, cancellationToken);
        return GroupId.Parse(response.Id ?? throw new SignalApiException(HttpStatusCode.OK, "The API did not return a group id."));
    }

    public Task UpdateAsync(PhoneNumber account, GroupId group, string? name = null, string? description = null, string? base64Avatar = null, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, GroupPath(account, group),
            new UpdateGroupRequestDto { Name = name, Description = description, Base64Avatar = base64Avatar },
            SignalRestJsonContext.Default.UpdateGroupRequestDto, cancellationToken);

    public Task AddMembersAsync(PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken = default) =>
        Members(HttpMethod.Post, account, group, members, cancellationToken);

    public Task RemoveMembersAsync(PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken = default) =>
        Members(HttpMethod.Delete, account, group, members, cancellationToken);

    public Task AddAdminsAsync(PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken = default) =>
        Admins(HttpMethod.Post, account, group, admins, cancellationToken);

    public Task RemoveAdminsAsync(PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken = default) =>
        Admins(HttpMethod.Delete, account, group, admins, cancellationToken);

    public Task QuitAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, $"{GroupPath(account, group)}/quit", cancellationToken);

    public Task DeleteAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Delete, GroupPath(account, group), cancellationToken);

    private Task Members(HttpMethod method, PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken) =>
        api.SendAsync(method, $"{GroupPath(account, group)}/members", new GroupMembersRequestDto { Members = [.. members] },
            SignalRestJsonContext.Default.GroupMembersRequestDto, cancellationToken);

    private Task Admins(HttpMethod method, PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken) =>
        api.SendAsync(method, $"{GroupPath(account, group)}/admins", new GroupAdminsRequestDto { Admins = [.. admins] },
            SignalRestJsonContext.Default.GroupAdminsRequestDto, cancellationToken);

    private static string GroupsPath(PhoneNumber account) => $"v1/groups/{SignalApiClient.Escape(account.Value)}";

    private static string GroupPath(PhoneNumber account, GroupId group) => $"{GroupsPath(account)}/{SignalApiClient.Escape(group.Value)}";

    private static Group? ToEntity(GroupDto dto) =>
        GroupId.TryParse(dto.Id, out var id)
            ? new Group(id, dto.Name ?? string.Empty, dto.Members ?? [], dto.Admins ?? [], dto.Description, dto.Blocked, dto.PendingInvites, dto.InviteLink)
            : null;
}

/// <summary>Implements <see cref="IAccountService"/> with <c>GET /v1/accounts</c> and <c>GET /v1/qrcodelink</c>.</summary>
internal sealed class RestAccountService(SignalApiClient api) : IAccountService
{
    public async Task<IReadOnlyList<PhoneNumber>> ListAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await api.GetAsync("v1/accounts", SignalRestJsonContext.Default.ListString, cancellationToken);
        return [.. accounts.Select(a => PhoneNumber.TryParse(a, out var n) ? n : (PhoneNumber?)null).OfType<PhoneNumber>()];
    }

    public Task<byte[]> GetLinkQrCodeAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        return api.GetBytesAsync($"v1/qrcodelink?device_name={Uri.EscapeDataString(deviceName)}", cancellationToken);
    }
}

/// <summary>Implements <see cref="IContactService"/> with <c>GET</c>/<c>PUT /v1/contacts/{number}</c>.</summary>
internal sealed class RestContactService(SignalApiClient api) : IContactService
{
    public async Task<IReadOnlyList<Contact>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default)
    {
        var contacts = await api.GetAsync($"v1/contacts/{SignalApiClient.Escape(account.Value)}", SignalRestJsonContext.Default.ListContactDto, cancellationToken);
        return [.. contacts
            .Where(c => (c.Number ?? c.Uuid) is not null)
            .Select(c => new Contact((c.Uuid ?? c.Number)!, c.Number, Guid.TryParse(c.Uuid, out var u) ? u : null, c.Name, c.ProfileName, c.Username, c.Blocked))];
    }

    public Task UpdateAsync(PhoneNumber account, Recipient contact, string? name, int? expirationInSeconds = null, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, $"v1/contacts/{SignalApiClient.Escape(account.Value)}",
            new UpdateContactRequestDto { Recipient = contact.Address, Name = name, ExpirationInSeconds = expirationInSeconds },
            SignalRestJsonContext.Default.UpdateContactRequestDto, cancellationToken);
}

/// <summary>Implements <see cref="IAttachmentService"/> with the <c>/v1/attachments</c> endpoints.</summary>
internal sealed class RestAttachmentService(SignalApiClient api) : IAttachmentService
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        await api.GetAsync("v1/attachments", SignalRestJsonContext.Default.ListString, cancellationToken);

    public Task<byte[]> DownloadAsync(string attachmentId, CancellationToken cancellationToken = default) =>
        api.GetBytesAsync($"v1/attachments/{SignalApiClient.Escape(attachmentId)}", cancellationToken);

    public Task DeleteAsync(string attachmentId, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Delete, $"v1/attachments/{SignalApiClient.Escape(attachmentId)}", cancellationToken);
}

/// <summary>Implements <see cref="IProfileService"/> with <c>PUT /v1/profiles/{number}</c>.</summary>
internal sealed class RestProfileService(SignalApiClient api) : IProfileService
{
    public Task UpdateAsync(PhoneNumber account, ProfileUpdate profile, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, $"v1/profiles/{SignalApiClient.Escape(account.Value)}",
            new UpdateProfileRequestDto { Name = profile.Name, About = profile.About, Base64Avatar = profile.Base64Avatar },
            SignalRestJsonContext.Default.UpdateProfileRequestDto, cancellationToken);
}

/// <summary>Implements <see cref="IIdentityService"/> with the <c>/v1/identities/{number}</c> endpoints.</summary>
internal sealed class RestIdentityService(SignalApiClient api) : IIdentityService
{
    public async Task<IReadOnlyList<Identity>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default)
    {
        var identities = await api.GetAsync($"v1/identities/{SignalApiClient.Escape(account.Value)}", SignalRestJsonContext.Default.ListIdentityDto, cancellationToken);
        return [.. identities
            .Where(i => (i.Number ?? i.Uuid) is not null)
            .Select(i => new Identity((i.Uuid ?? i.Number)!, i.Number, Guid.TryParse(i.Uuid, out var u) ? u : null,
                i.Status ?? "UNKNOWN", i.Fingerprint, i.SafetyNumber, i.Added))];
    }

    public Task TrustAsync(PhoneNumber account, PhoneNumber numberToTrust, string? verifiedSafetyNumber = null, bool trustAllKnownKeys = false, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put,
            $"v1/identities/{SignalApiClient.Escape(account.Value)}/trust/{SignalApiClient.Escape(numberToTrust.Value)}",
            new TrustIdentityRequestDto { VerifiedSafetyNumber = verifiedSafetyNumber, TrustAllKnownKeys = trustAllKnownKeys ? true : null },
            SignalRestJsonContext.Default.TrustIdentityRequestDto, cancellationToken);
}

/// <summary>Implements <see cref="ISystemService"/> with <c>GET /v1/about</c> and <c>GET /v1/health</c>.</summary>
internal sealed class RestSystemService(SignalApiClient api) : ISystemService
{
    public async Task<SignalApiInfo> GetAboutAsync(CancellationToken cancellationToken = default)
    {
        var about = await api.GetAsync("v1/about", SignalRestJsonContext.Default.AboutDto, cancellationToken);
        return new SignalApiInfo(
            about.Version,
            about.Build,
            about.Mode,
            about.Versions ?? [],
            (about.Capabilities ?? []).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value));
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await api.IsSuccessAsync("v1/health", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
