using Signal.Domain;
using Signal.Domain.Entities;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Abstractions;

// Ports: the application layer's view of the outside world. The infrastructure layer implements each
// of them on top of one or more signal-cli-rest-api endpoints (noted in the member documentation).
// Replace any of them in DI to change the transport or to fake Signal in tests.

/// <summary>Result of a successful send.</summary>
/// <param name="Timestamp">
/// Timestamp assigned to the sent message (Unix milliseconds). Keep it to edit, react to, quote or delete the message later.
/// </param>
public readonly record struct SendResult(long Timestamp);

/// <summary>Sends and deletes messages.</summary>
public interface IMessageSender
{
    /// <summary>Sends a message (<c>POST /v2/send</c>).</summary>
    /// <param name="account">The sending account.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The timestamp of the sent message.</returns>
    /// <exception cref="SignalApiException">The API rejected the message (e.g. unregistered recipient).</exception>
    Task<SendResult> SendAsync(PhoneNumber account, OutgoingMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message the account sent earlier, for everyone in the conversation (<c>DELETE /v1/remote-delete/{number}</c>).
    /// Recipients see "This message was deleted".
    /// </summary>
    /// <remarks>
    /// Only the account's own messages can be deleted, and Signal clients honor remote deletes only for a limited
    /// time after sending. The default implementation throws <see cref="NotSupportedException"/>, so existing
    /// <see cref="IMessageSender"/> implementations keep compiling.
    /// </remarks>
    /// <example>
    /// <code>
    /// var sent = await sender.SendAsync(account, OutgoingMessage.To(group).WithText("Oops").Build());
    /// await sender.RemoteDeleteAsync(account, group, sent.Timestamp);
    /// </code>
    /// </example>
    /// <param name="account">The account that sent the message.</param>
    /// <param name="recipient">The conversation the message was sent to (the group, or the direct-message recipient).</param>
    /// <param name="targetTimestamp">The <see cref="SendResult.Timestamp"/> of the message to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The timestamp of the delete message itself.</returns>
    /// <exception cref="SignalApiException">The API rejected the delete.</exception>
    /// <exception cref="NotSupportedException">The implementation does not support remote deletes.</exception>
    Task<SendResult> RemoteDeleteAsync(PhoneNumber account, Recipient recipient, long targetTimestamp, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support remote deletes.");
}

/// <summary>
/// A source of incoming envelopes for one account. The transport depends on the <see cref="ExecutionMode"/>:
/// HTTP polling for <c>normal</c>/<c>native</c>, a WebSocket for <c>json-rpc</c>/<c>json-rpc-native</c>.
/// </summary>
public interface IMessageReceiver
{
    /// <summary>
    /// Streams envelopes until <paramref name="cancellationToken"/> is cancelled. Implementations handle transient
    /// failures (reconnects, retries) internally and only complete when cancelled.
    /// </summary>
    /// <param name="account">The account to receive for.</param>
    /// <param name="cancellationToken">Stops receiving.</param>
    /// <returns>An endless stream of envelopes.</returns>
    IAsyncEnumerable<IncomingEnvelope> ReceiveAsync(PhoneNumber account, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the <see cref="IMessageReceiver"/> responsible for an <see cref="ExecutionMode"/>.</summary>
public interface IMessageReceiverFactory
{
    /// <summary>Returns the receiver registered for <paramref name="mode"/>.</summary>
    /// <param name="mode">The container's execution mode.</param>
    /// <returns>The receiver.</returns>
    /// <exception cref="InvalidOperationException">No receiver is registered for the mode.</exception>
    IMessageReceiver Create(ExecutionMode mode);
}

/// <summary>Sends and removes emoji reactions.</summary>
public interface IReactionService
{
    /// <summary>Reacts to a message (<c>POST /v1/reactions/{number}</c>).</summary>
    /// <param name="account">The reacting account.</param>
    /// <param name="recipient">The conversation of the target message (the group, or the author for direct messages).</param>
    /// <param name="emoji">The reaction emoji.</param>
    /// <param name="targetAuthor">Phone number or UUID of the author of the target message.</param>
    /// <param name="targetTimestamp">Timestamp of the target message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reaction was sent.</returns>
    Task SendReactionAsync(PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction (<c>DELETE /v1/reactions/{number}</c>).</summary>
    /// <param name="account">The reacting account.</param>
    /// <param name="recipient">The conversation of the target message.</param>
    /// <param name="emoji">The reaction emoji to remove.</param>
    /// <param name="targetAuthor">Phone number or UUID of the author of the target message.</param>
    /// <param name="targetTimestamp">Timestamp of the target message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the removal was sent.</returns>
    Task RemoveReactionAsync(PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default);
}

/// <summary>Sends read and viewed receipts.</summary>
public interface IReceiptService
{
    /// <summary>Sends a receipt (<c>POST /v1/receipts/{number}</c>).</summary>
    /// <param name="account">The account sending the receipt.</param>
    /// <param name="recipient">The author of the acknowledged message.</param>
    /// <param name="timestamp">Timestamp of the acknowledged message.</param>
    /// <param name="type"><see cref="ReceiptType.Read"/> or <see cref="ReceiptType.Viewed"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the receipt was sent.</returns>
    /// <exception cref="ArgumentException"><paramref name="type"/> is <see cref="ReceiptType.Delivery"/> (sent automatically by Signal).</exception>
    Task SendReceiptAsync(PhoneNumber account, Recipient recipient, long timestamp, ReceiptType type = ReceiptType.Read, CancellationToken cancellationToken = default);
}

/// <summary>Shows and hides the "typing…" indicator.</summary>
public interface ITypingIndicatorService
{
    /// <summary>Shows the typing indicator (<c>PUT /v1/typing-indicator/{number}</c>).</summary>
    /// <param name="account">The typing account.</param>
    /// <param name="recipient">The conversation to show the indicator in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the indicator was sent.</returns>
    Task StartTypingAsync(PhoneNumber account, Recipient recipient, CancellationToken cancellationToken = default);

    /// <summary>Hides the typing indicator (<c>DELETE /v1/typing-indicator/{number}</c>).</summary>
    /// <param name="account">The typing account.</param>
    /// <param name="recipient">The conversation to hide the indicator in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the indicator was sent.</returns>
    Task StopTypingAsync(PhoneNumber account, Recipient recipient, CancellationToken cancellationToken = default);
}

/// <summary>Manages groups (<c>/v1/groups/{number}</c>).</summary>
public interface IGroupService
{
    /// <summary>Lists all groups the account is a member of.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The groups.</returns>
    Task<IReadOnlyList<Group>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);

    /// <summary>Gets one group.</summary>
    /// <param name="account">The account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The group, or <see langword="null"/> if it is unknown to the account.</returns>
    Task<Group?> GetAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default);

    /// <summary>Creates a group.</summary>
    /// <param name="account">The creating account (becomes admin).</param>
    /// <param name="name">The group name.</param>
    /// <param name="members">Initial members (phone numbers or UUIDs).</param>
    /// <param name="description">Optional description.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The id of the new group.</returns>
    Task<GroupId> CreateAsync(PhoneNumber account, string name, IEnumerable<string> members, string? description = null, CancellationToken cancellationToken = default);

    /// <summary>Updates name, description and/or avatar. <see langword="null"/> values are left unchanged.</summary>
    /// <param name="account">The account (must be allowed to edit the group).</param>
    /// <param name="group">The group id.</param>
    /// <param name="name">New name, or <see langword="null"/>.</param>
    /// <param name="description">New description, or <see langword="null"/>.</param>
    /// <param name="base64Avatar">New avatar image as base64, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the group was updated.</returns>
    Task UpdateAsync(PhoneNumber account, GroupId group, string? name = null, string? description = null, string? base64Avatar = null, CancellationToken cancellationToken = default);

    /// <summary>Adds members.</summary>
    /// <param name="account">The account (must be allowed to add members).</param>
    /// <param name="group">The group id.</param>
    /// <param name="members">Members to add (phone numbers or UUIDs).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the members were added.</returns>
    Task AddMembersAsync(PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken = default);

    /// <summary>Removes members.</summary>
    /// <param name="account">The account (must be a group admin).</param>
    /// <param name="group">The group id.</param>
    /// <param name="members">Members to remove (phone numbers or UUIDs).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the members were removed.</returns>
    Task RemoveMembersAsync(PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken = default);

    /// <summary>Promotes members to admins.</summary>
    /// <param name="account">The account (must be a group admin).</param>
    /// <param name="group">The group id.</param>
    /// <param name="admins">Members to promote.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the members were promoted.</returns>
    Task AddAdminsAsync(PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken = default);

    /// <summary>Demotes admins to regular members.</summary>
    /// <param name="account">The account (must be a group admin).</param>
    /// <param name="group">The group id.</param>
    /// <param name="admins">Admins to demote.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the admins were demoted.</returns>
    Task RemoveAdminsAsync(PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken = default);

    /// <summary>Leaves the group.</summary>
    /// <param name="account">The leaving account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the account left.</returns>
    Task QuitAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default);

    /// <summary>Deletes the group from the account's local storage.</summary>
    /// <param name="account">The account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the group was deleted.</returns>
    Task DeleteAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default);
}

/// <summary>Lists accounts and links new devices.</summary>
public interface IAccountService
{
    /// <summary>Lists the accounts registered or linked in the container (<c>GET /v1/accounts</c>).</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The account phone numbers.</returns>
    Task<IReadOnlyList<PhoneNumber>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a PNG QR code for linking the container as a secondary device of an existing Signal account
    /// (<c>GET /v1/qrcodelink</c>). Scan it in the Signal app under "Linked devices".
    /// </summary>
    /// <param name="deviceName">Name shown for the linked device in the Signal app.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The PNG image bytes.</returns>
    Task<byte[]> GetLinkQrCodeAsync(string deviceName, CancellationToken cancellationToken = default);
}

/// <summary>Reads and updates the account's contacts (<c>/v1/contacts/{number}</c>).</summary>
public interface IContactService
{
    /// <summary>Lists the account's contacts.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The contacts.</returns>
    Task<IReadOnlyList<Contact>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a contact.</summary>
    /// <param name="account">The account.</param>
    /// <param name="contact">The contact to update.</param>
    /// <param name="name">The name to store for the contact.</param>
    /// <param name="expirationInSeconds">Disappearing-messages timer for the conversation, or <see langword="null"/> to keep it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the contact was updated.</returns>
    Task UpdateAsync(PhoneNumber account, Recipient contact, string? name, int? expirationInSeconds = null, CancellationToken cancellationToken = default);
}

/// <summary>Accesses attachments stored by the container (<c>/v1/attachments</c>).</summary>
public interface IAttachmentService
{
    /// <summary>Lists the ids of all stored attachments.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The attachment ids.</returns>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads an attachment completely into memory. Prefer <see cref="OpenReadAsync"/> for large files.</summary>
    /// <param name="attachmentId">The id from <see cref="Attachment.Id"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The attachment content.</returns>
    Task<byte[]> DownloadAsync(string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an attachment as a stream that reads directly from the HTTP response (<c>GET /v1/attachments/{id}</c>),
    /// so large files never have to fit in memory. Dispose the result to release the connection.
    /// </summary>
    /// <remarks>
    /// The default implementation falls back to <see cref="DownloadAsync"/> and wraps the bytes in a
    /// <see cref="MemoryStream"/>, so existing <see cref="IAttachmentService"/> implementations keep working.
    /// </remarks>
    /// <example>
    /// <code>
    /// await using var download = await attachments.OpenReadAsync(attachment.Id, ct);
    /// await using var file = File.Create(attachment.Filename ?? attachment.Id);
    /// await download.Content.CopyToAsync(file, ct);
    /// </code>
    /// </example>
    /// <param name="attachmentId">The id from <see cref="Attachment.Id"/>.</param>
    /// <param name="cancellationToken">Cancels opening the stream (reads take their own token).</param>
    /// <returns>The open download. The caller owns and must dispose it.</returns>
    /// <exception cref="SignalApiException">The attachment does not exist (404) or the API rejected the request.</exception>
    async Task<AttachmentDownload> OpenReadAsync(string attachmentId, CancellationToken cancellationToken = default) =>
        new(new MemoryStream(await DownloadAsync(attachmentId, cancellationToken), writable: false), ContentType: null, Length: null);

    /// <summary>Deletes a stored attachment to free disk space in the container.</summary>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the attachment was deleted.</returns>
    Task DeleteAsync(string attachmentId, CancellationToken cancellationToken = default);
}

/// <summary>An attachment opened for streaming by <see cref="IAttachmentService.OpenReadAsync"/>.</summary>
/// <remarks>Disposing it disposes <see cref="Content"/>, which releases the underlying HTTP response.</remarks>
/// <param name="Content">The attachment bytes as a forward-only, read-only stream.</param>
/// <param name="ContentType">The MIME type reported by the API, if any.</param>
/// <param name="Length">The size in bytes reported by the API, if known.</param>
public sealed record AttachmentDownload(Stream Content, string? ContentType, long? Length) : IAsyncDisposable, IDisposable
{
    /// <summary>Disposes <see cref="Content"/>.</summary>
    /// <returns>A task that completes when the stream is disposed.</returns>
    public ValueTask DisposeAsync() => Content.DisposeAsync();

    /// <summary>Disposes <see cref="Content"/>.</summary>
    public void Dispose() => Content.Dispose();
}

/// <summary>New profile values for an account.</summary>
/// <param name="Name">The profile name shown to other users.</param>
/// <param name="About">The "about" text.</param>
/// <param name="Base64Avatar">The avatar image as base64.</param>
public sealed record ProfileUpdate(string Name, string? About = null, string? Base64Avatar = null);

/// <summary>Updates the account's Signal profile.</summary>
public interface IProfileService
{
    /// <summary>Updates the profile (<c>PUT /v1/profiles/{number}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="profile">The new profile values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the profile was updated.</returns>
    Task UpdateAsync(PhoneNumber account, ProfileUpdate profile, CancellationToken cancellationToken = default);
}

/// <summary>Inspects and trusts identity keys (safety numbers).</summary>
public interface IIdentityService
{
    /// <summary>Lists known identities (<c>GET /v1/identities/{number}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The identities.</returns>
    Task<IReadOnlyList<Identity>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);

    /// <summary>Trusts an identity (<c>PUT /v1/identities/{number}/trust/{numberToTrust}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="numberToTrust">The contact whose key to trust.</param>
    /// <param name="verifiedSafetyNumber">The safety number verified out of band; marks the key as verified.</param>
    /// <param name="trustAllKnownKeys">Trust all known keys of the contact without verification.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the key was trusted.</returns>
    Task TrustAsync(PhoneNumber account, PhoneNumber numberToTrust, string? verifiedSafetyNumber = null, bool trustAllKnownKeys = false, CancellationToken cancellationToken = default);
}

/// <summary>Information about the running signal-cli-rest-api, from <c>GET /v1/about</c>.</summary>
/// <param name="Version">The API version, e.g. <c>0.92</c>.</param>
/// <param name="Build">The build number.</param>
/// <param name="RawMode">The container mode as reported, e.g. <c>json-rpc</c>.</param>
/// <param name="ApiVersions">Supported API versions, e.g. <c>v1</c>, <c>v2</c>.</param>
/// <param name="Capabilities">Supported features per endpoint, e.g. <c>v2/send</c> → <c>quotes</c>, <c>mentions</c>.</param>
public sealed record SignalApiInfo(
    string? Version,
    int Build,
    string? RawMode,
    IReadOnlyList<string> ApiVersions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Capabilities)
{
    /// <summary><see cref="RawMode"/> as <see cref="ExecutionMode"/>, or <see langword="null"/> if unknown.</summary>
    public ExecutionMode? Mode => ExecutionModeExtensions.TryParseContainerValue(RawMode, out var mode) ? mode : null;
}

/// <summary>Container-level information and health.</summary>
public interface ISystemService
{
    /// <summary>Reads version, mode and capabilities (<c>GET /v1/about</c>).</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The API information.</returns>
    Task<SignalApiInfo> GetAboutAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks the container health (<c>GET /v1/health</c>). Never throws for connection failures.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> if the API reports healthy.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
