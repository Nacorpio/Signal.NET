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

    // The members below were added after 0.2.0-preview.2. Their default implementations throw
    // NotSupportedException, so IGroupService implementations written earlier keep compiling.

    /// <summary>Accepts an invitation to a group (<c>POST /v1/groups/{number}/{groupid}/join</c>).</summary>
    /// <param name="account">The invited account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the account joined.</returns>
    Task JoinAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support joining groups.");

    /// <summary>Blocks a group, so its messages are no longer received (<c>POST /v1/groups/{number}/{groupid}/block</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the group was blocked.</returns>
    Task BlockAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support blocking groups.");

    /// <summary>
    /// Changes permissions, the invite link mode and/or the disappearing-messages timer
    /// (<c>PUT /v1/groups/{number}/{groupid}</c>). <see langword="null"/> values are left unchanged.
    /// </summary>
    /// <param name="account">The account (usually must be a group admin).</param>
    /// <param name="group">The group id.</param>
    /// <param name="settings">The settings to change.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the group was updated.</returns>
    Task UpdateSettingsAsync(PhoneNumber account, GroupId group, GroupSettings settings, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support group settings.");

    /// <summary>Pins a message in a group (<c>POST /v1/groups/{number}/{groupid}/pin-message</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="targetAuthor">Phone number or UUID of the pinned message's author.</param>
    /// <param name="targetTimestamp">Timestamp of the pinned message.</param>
    /// <param name="duration">How long the message stays pinned (whole seconds), or <see langword="null"/> for the API default.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the message was pinned.</returns>
    /// <exception cref="ArgumentException"><paramref name="targetAuthor"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is shorter than one second.</exception>
    Task PinMessageAsync(PhoneNumber account, GroupId group, string targetAuthor, long targetTimestamp, TimeSpan? duration = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support pinned messages.");

    /// <summary>Unpins a message in a group (<c>DELETE /v1/groups/{number}/{groupid}/pin-message</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="group">The group id.</param>
    /// <param name="targetAuthor">Phone number or UUID of the pinned message's author.</param>
    /// <param name="targetTimestamp">Timestamp of the pinned message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the message was unpinned.</returns>
    /// <exception cref="ArgumentException"><paramref name="targetAuthor"/> is empty.</exception>
    Task UnpinMessageAsync(PhoneNumber account, GroupId group, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support pinned messages.");
}

/// <summary>Who may perform an action in a group.</summary>
public enum GroupPermission
{
    /// <summary>Every member (<c>every-member</c>).</summary>
    EveryMember,

    /// <summary>Only admins (<c>only-admins</c>).</summary>
    OnlyAdmins,
}

/// <summary>Whether and how people can join a group through its invite link.</summary>
public enum GroupLinkMode
{
    /// <summary>The invite link is disabled.</summary>
    Disabled,

    /// <summary>Anyone with the link can join.</summary>
    Enabled,

    /// <summary>Anyone with the link can request to join; an admin must approve.</summary>
    EnabledWithApproval,
}

/// <summary>The permissions of a group. The API requires all three to be set together.</summary>
/// <param name="AddMembers">Who may add members.</param>
/// <param name="EditGroup">Who may edit name, description, avatar and timer.</param>
/// <param name="SendMessages">Who may send messages (<see cref="GroupPermission.OnlyAdmins"/> makes an announcement group).</param>
public sealed record GroupPermissions(GroupPermission AddMembers, GroupPermission EditGroup, GroupPermission SendMessages);

/// <summary>Group settings for <see cref="IGroupService.UpdateSettingsAsync"/>. <see langword="null"/> leaves a setting unchanged.</summary>
/// <param name="Permissions">The permissions.</param>
/// <param name="Link">The invite link mode.</param>
/// <param name="MessageExpiration">The disappearing-messages timer (whole seconds); <see cref="TimeSpan.Zero"/> turns it off.</param>
public sealed record GroupSettings(GroupPermissions? Permissions = null, GroupLinkMode? Link = null, TimeSpan? MessageExpiration = null);

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

    // The members below were added after 0.2.0-preview.2. Their default implementations throw
    // NotSupportedException, so IAccountService implementations written earlier keep compiling.

    /// <summary>
    /// Sets the account's username (<c>POST /v1/accounts/{number}/username</c>). Signal appends a numeric
    /// discriminator, so requesting <c>alice</c> results in e.g. <c>alice.42</c>.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="nickname">The requested name, without discriminator.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The assigned username and share link, or <see langword="null"/> if the API did not report them.</returns>
    /// <exception cref="ArgumentException"><paramref name="nickname"/> is empty.</exception>
    /// <exception cref="SignalApiException">The name is invalid or unavailable.</exception>
    Task<UsernameAssignment?> SetUsernameAsync(PhoneNumber account, string nickname, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support usernames.");

    /// <summary>Removes the account's username (<c>DELETE /v1/accounts/{number}/username</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the username was removed.</returns>
    Task DeleteUsernameAsync(PhoneNumber account, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support usernames.");

    /// <summary>Updates privacy settings (<c>PUT /v1/accounts/{number}/settings</c>). <see langword="null"/> values are left unchanged.</summary>
    /// <param name="account">The account.</param>
    /// <param name="settings">The settings to change.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the settings were updated.</returns>
    Task UpdateSettingsAsync(PhoneNumber account, AccountSettings settings, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support account settings.");

    /// <summary>
    /// Sets the registration lock PIN (<c>POST /v1/accounts/{number}/pin</c>). The PIN is then required to register
    /// the number again (see <c>IRegistrationService.VerifyAsync</c>).
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="pin">The new PIN.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the PIN was set.</returns>
    /// <exception cref="ArgumentException"><paramref name="pin"/> is empty.</exception>
    Task SetPinAsync(PhoneNumber account, string pin, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support registration lock PINs.");

    /// <summary>Removes the registration lock PIN (<c>DELETE /v1/accounts/{number}/pin</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the PIN was removed.</returns>
    Task RemovePinAsync(PhoneNumber account, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support registration lock PINs.");

    /// <summary>
    /// Lifts a rate limit imposed by Signal by submitting a solved captcha
    /// (<c>POST /v1/accounts/{number}/rate-limit-challenge</c>).
    /// </summary>
    /// <remarks>
    /// When sending fails with a rate-limit error, Signal provides a challenge token. Solve the captcha at
    /// <c>https://signalcaptchas.org/challenge/generate.html</c> and submit both.
    /// </remarks>
    /// <param name="account">The rate-limited account.</param>
    /// <param name="challengeToken">The challenge token from the rate-limit error.</param>
    /// <param name="captcha">The solved captcha (<c>signalcaptcha://…</c>).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the challenge was accepted.</returns>
    /// <exception cref="ArgumentException">A token or captcha is empty.</exception>
    Task SubmitRateLimitChallengeAsync(PhoneNumber account, string challengeToken, string captcha, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support rate-limit challenges.");

    /// <summary>
    /// Gets the raw device link URI (<c>sgnl://linkdevice?…</c>) for linking the container as a secondary device
    /// (<c>GET /v1/qrcodelink/raw</c>). Use it to render your own QR code instead of <see cref="GetLinkQrCodeAsync"/>.
    /// </summary>
    /// <param name="deviceName">Name shown for the linked device in the Signal app.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The device link URI.</returns>
    /// <exception cref="ArgumentException"><paramref name="deviceName"/> is empty.</exception>
    Task<string> GetLinkUriAsync(string deviceName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support raw device link URIs.");
}

/// <summary>A sticker pack known to an account, from <see cref="IStickerService.ListAsync"/>.</summary>
/// <param name="PackId">The hex pack id; use it with <c>OutgoingMessageBuilder.WithSticker</c>.</param>
/// <param name="Title">The pack title, if known.</param>
/// <param name="Author">The pack author, if known.</param>
/// <param name="Installed">Whether the pack is installed; only stickers of installed packs can be sent.</param>
/// <param name="Url">The pack's share URL, if reported.</param>
public sealed record StickerPack(string PackId, string? Title, string? Author, bool Installed, string? Url);

/// <summary>Lists and installs sticker packs (<c>/v1/sticker-packs/{number}</c>).</summary>
public interface IStickerService
{
    /// <summary>Lists the account's sticker packs (<c>GET /v1/sticker-packs/{number}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The sticker packs.</returns>
    Task<IReadOnlyList<StickerPack>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);

    /// <summary>Installs a sticker pack (<c>POST /v1/sticker-packs/{number}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="packId">The hex pack id.</param>
    /// <param name="packKey">The hex pack key that decrypts the pack.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the pack was installed.</returns>
    /// <exception cref="ArgumentException"><paramref name="packId"/> or <paramref name="packKey"/> is empty.</exception>
    Task InstallAsync(PhoneNumber account, string packId, string packKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a sticker pack from its share link, <c>https://signal.art/addstickers/#pack_id=…&amp;pack_key=…</c>.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="addStickersUrl">The share link.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the pack was installed.</returns>
    /// <exception cref="ArgumentException">The link has no <c>pack_id</c> or <c>pack_key</c>.</exception>
    Task InstallAsync(PhoneNumber account, Uri addStickersUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addStickersUrl);

        // The parameters are in the fragment (never sent to signal.art), formatted like a query string.
        string? packId = null, packKey = null;
        foreach (var pair in addStickersUrl.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (pair.Split('=', 2))
            {
                case ["pack_id", var value]:
                    packId = Uri.UnescapeDataString(value);
                    break;
                case ["pack_key", var value]:
                    packKey = Uri.UnescapeDataString(value);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(packKey))
        {
            throw new ArgumentException("The link must contain pack_id and pack_key.", nameof(addStickersUrl));
        }

        return InstallAsync(account, packId, packKey, cancellationToken);
    }
}

/// <summary>A device linked to an account, from <see cref="IDeviceService.ListAsync"/>.</summary>
/// <param name="Id">The device id; the primary device is <c>1</c>.</param>
/// <param name="Name">The device name, if set.</param>
/// <param name="Created">When the device was linked, if reported.</param>
/// <param name="LastSeen">When the device was last active, if reported.</param>
public sealed record LinkedDevice(long Id, string? Name, DateTimeOffset? Created, DateTimeOffset? LastSeen)
{
    /// <summary>The id of an account's primary device (the phone that registered the number).</summary>
    public const long PrimaryDeviceId = 1;

    /// <summary>Whether this is the account's primary device.</summary>
    public bool IsPrimary => Id == PrimaryDeviceId;
}

/// <summary>
/// Manages the devices linked to an account that is registered in the container (<c>/v1/devices/{number}</c>), i.e.
/// the container acts as the primary device. To link the container itself to another account, use
/// <see cref="IAccountService.GetLinkQrCodeAsync"/> instead.
/// </summary>
public interface IDeviceService
{
    /// <summary>Lists the account's devices (<c>GET /v1/devices/{number}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The devices, including the primary device.</returns>
    Task<IReadOnlyList<LinkedDevice>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a new device to the account (<c>POST /v1/devices/{number}</c>), like scanning its QR code in the Signal app.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="deviceLinkUri">The URI encoded in the new device's QR code (<c>sgnl://linkdevice?…</c>).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the device was linked.</returns>
    /// <exception cref="ArgumentException"><paramref name="deviceLinkUri"/> is empty.</exception>
    Task LinkAsync(PhoneNumber account, string deviceLinkUri, CancellationToken cancellationToken = default);

    /// <summary>Unlinks a device from the account (<c>DELETE /v1/devices/{number}/{deviceId}</c>).</summary>
    /// <param name="account">The account.</param>
    /// <param name="deviceId">The id of the device to remove, from <see cref="ListAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the device was removed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deviceId"/> is the primary device or not positive.</exception>
    Task RemoveAsync(PhoneNumber account, long deviceId, CancellationToken cancellationToken = default);
}

/// <summary>The result of <see cref="IAccountService.SetUsernameAsync"/>.</summary>
/// <param name="Username">The assigned username including its discriminator, e.g. <c>alice.42</c>.</param>
/// <param name="Link">A shareable link that opens a chat with the account, if provided.</param>
public sealed record UsernameAssignment(Username Username, string? Link);

/// <summary>Account privacy settings for <see cref="IAccountService.UpdateSettingsAsync"/>. <see langword="null"/> leaves a setting unchanged.</summary>
/// <param name="DiscoverableByNumber">Whether people who have the phone number can find the account.</param>
/// <param name="ShareNumber">Whether the phone number is shown to people the account messages.</param>
public sealed record AccountSettings(bool? DiscoverableByNumber = null, bool? ShareNumber = null);

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

    // The members below were added after 0.2.0-preview.2. Their default implementations throw
    // NotSupportedException, so IContactService implementations written earlier keep compiling.

    /// <summary>
    /// Sends the account's contact list to its linked devices (<c>POST /v1/contacts/{number}/sync</c>).
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the sync message was sent.</returns>
    Task SyncAsync(PhoneNumber account, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support contact sync.");

    /// <summary>Checks which phone numbers are registered with Signal (<c>GET /v1/search/{number}</c>).</summary>
    /// <param name="account">The account performing the lookup.</param>
    /// <param name="numbers">The numbers to check. Duplicates are checked once; an empty list makes no request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One entry per number the API reported on.</returns>
    Task<IReadOnlyList<NumberRegistration>> CheckRegisteredAsync(PhoneNumber account, IEnumerable<PhoneNumber> numbers, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} does not support number search.");
}

/// <summary>Whether a phone number is registered with Signal, from <see cref="IContactService.CheckRegisteredAsync"/>.</summary>
/// <param name="Number">The checked number.</param>
/// <param name="IsRegistered">Whether the number can receive Signal messages.</param>
public sealed record NumberRegistration(PhoneNumber Number, bool IsRegistered);

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
