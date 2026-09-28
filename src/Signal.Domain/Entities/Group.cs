using Signal.Domain.ValueObjects;

namespace Signal.Domain.Entities;

/// <summary>A snapshot of a Signal group as known to the account.</summary>
/// <param name="id">The group id.</param>
/// <param name="name">The group name.</param>
/// <param name="members">Member identifiers (phone numbers or UUIDs).</param>
/// <param name="admins">Admin identifiers (phone numbers or UUIDs).</param>
/// <param name="description">The group description.</param>
/// <param name="blocked">Whether the account blocked the group.</param>
/// <param name="pendingInvites">Identifiers of invited users who have not joined yet.</param>
/// <param name="inviteLink">The group invite link, if enabled.</param>
public sealed class Group(
    GroupId id,
    string name,
    IEnumerable<string> members,
    IEnumerable<string> admins,
    string? description = null,
    bool blocked = false,
    IEnumerable<string>? pendingInvites = null,
    string? inviteLink = null) : Entity<GroupId>(id)
{
    /// <summary>The group name.</summary>
    public string Name { get; } = name;

    /// <summary>The group description.</summary>
    public string? Description { get; } = description;

    /// <summary>Member identifiers (phone numbers or UUIDs), compared case-insensitively.</summary>
    public IReadOnlySet<string> Members { get; } = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);

    /// <summary>Admin identifiers (phone numbers or UUIDs), compared case-insensitively.</summary>
    public IReadOnlySet<string> Admins { get; } = new HashSet<string>(admins, StringComparer.OrdinalIgnoreCase);

    /// <summary>Identifiers of invited users who have not joined yet.</summary>
    public IReadOnlySet<string> PendingInvites { get; } = new HashSet<string>(pendingInvites ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the account blocked the group.</summary>
    public bool IsBlocked { get; } = blocked;

    /// <summary>The group invite link, if enabled.</summary>
    public string? InviteLink { get; } = inviteLink;

    /// <summary>Whether <paramref name="identifier"/> is a member.</summary>
    /// <param name="identifier">A phone number or UUID.</param>
    /// <returns><see langword="true"/> if the identifier is in <see cref="Members"/>.</returns>
    public bool HasMember(string identifier) => Members.Contains(identifier);

    /// <summary>Whether <paramref name="identifier"/> is an admin.</summary>
    /// <param name="identifier">A phone number or UUID.</param>
    /// <returns><see langword="true"/> if the identifier is in <see cref="Admins"/>.</returns>
    public bool IsAdmin(string identifier) => Admins.Contains(identifier);
}
