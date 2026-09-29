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
/// Timestamp assigned to the sent message (Unix milliseconds). Keep it to edit, react to, or quote the message later.
/// </param>
public readonly record struct SendResult(long Timestamp);

/// <summary>Sends messages.</summary>
public interface IMessageSender
{
    /// <summary>Sends a message (<c>POST /v2/send</c>).</summary>
    /// <param name="account">The sending account.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The timestamp of the sent message.</returns>
    /// <exception cref="SignalApiException">The API rejected the message (e.g. unregistered recipient).</exception>
    Task<SendResult> SendAsync(PhoneNumber account, OutgoingMessage message, CancellationToken cancellationToken = default);
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

/// <summary>How a verification code is requested when registering a number.</summary>
/// <param name="UseVoice">Deliver the code by voice call instead of SMS (e.g. for landlines).</param>
/// <param name="Captcha">
/// A captcha token, required when Signal rejects the registration with a captcha error. Solve the captcha at
/// <c>https://signalcaptchas.org/registration/generate.html</c> and pass the resulting <c>signalcaptcha://…</c> link.
/// </param>
public sealed record RegistrationOptions(bool UseVoice = false, string? Captcha = null);

/// <summary>
/// Registers a phone number as the container's primary Signal device, as an alternative to linking the container
/// to an existing account (<see cref="IAccountService.GetLinkQrCodeAsync"/>).
/// </summary>
/// <remarks>
/// <para>Typical flow: <see cref="RegisterAsync"/> sends a code by SMS or voice, then <see cref="VerifyAsync"/> completes the
/// registration with that code. Registering a number that is active on a phone moves the account to the container:
/// the phone's Signal app is signed out.</para>
/// <para>Calls are never retried (they are POST requests): a retry would request another code and can run into
/// Signal's rate limits.</para>
/// </remarks>
public interface IRegistrationService
{
    /// <summary>Requests a verification code for <paramref name="number"/> (<c>POST /v1/register/{number}</c>).</summary>
    /// <param name="number">The number to register.</param>
    /// <param name="options">Voice instead of SMS, and an optional captcha token; defaults to SMS without captcha.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the code was requested.</returns>
    /// <exception cref="SignalApiException">
    /// The API rejected the request, e.g. because a captcha is required (retry with <see cref="RegistrationOptions.Captcha"/>).
    /// </exception>
    Task RegisterAsync(PhoneNumber number, RegistrationOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Completes a registration with the received code (<c>POST /v1/register/{number}/verify/{code}</c>).</summary>
    /// <param name="number">The number being registered.</param>
    /// <param name="verificationCode">The code from the SMS or call; separators such as <c>123-456</c> are removed.</param>
    /// <param name="pin">The registration lock PIN, if the account has one.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the number is registered.</returns>
    /// <exception cref="ArgumentException"><paramref name="verificationCode"/> is empty.</exception>
    /// <exception cref="SignalApiException">The code is wrong or expired, or the PIN is missing or wrong.</exception>
    Task VerifyAsync(PhoneNumber number, string verificationCode, string? pin = null, CancellationToken cancellationToken = default);

    /// <summary>Unregisters a number from the container (<c>POST /v1/unregister/{number}</c>).</summary>
    /// <param name="number">The registered number.</param>
    /// <param name="deleteAccount">Also delete the Signal account on Signal's servers. This cannot be undone.</param>
    /// <param name="deleteLocalData">Also delete the account's local data (keys, messages) from the container.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the number is unregistered.</returns>
    Task UnregisterAsync(PhoneNumber number, bool deleteAccount = false, bool deleteLocalData = false, CancellationToken cancellationToken = default);
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

    /// <summary>Downloads an attachment.</summary>
    /// <param name="attachmentId">The id from <see cref="Attachment.Id"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The attachment content.</returns>
    Task<byte[]> DownloadAsync(string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a stored attachment to free disk space in the container.</summary>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the attachment was deleted.</returns>
    Task DeleteAsync(string attachmentId, CancellationToken cancellationToken = default);
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
