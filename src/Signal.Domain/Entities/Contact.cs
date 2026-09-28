namespace Signal.Domain.Entities;

/// <summary>An entry in the account's contact list.</summary>
/// <param name="id">The identity of the contact (its UUID, otherwise its phone number).</param>
/// <param name="number">The phone number, if known.</param>
/// <param name="uuid">The account identifier (ACI), if known.</param>
/// <param name="name">The name given to the contact by the account owner.</param>
/// <param name="profileName">The name the contact set in their Signal profile.</param>
/// <param name="username">The contact's Signal username.</param>
/// <param name="blocked">Whether the account blocked the contact.</param>
public sealed class Contact(string id, string? number, Guid? uuid, string? name, string? profileName, string? username, bool blocked)
    : Entity<string>(id)
{
    /// <summary>The phone number, if known.</summary>
    public string? Number { get; } = number;

    /// <summary>The account identifier (ACI), if known.</summary>
    public Guid? Uuid { get; } = uuid;

    /// <summary>The name given to the contact by the account owner.</summary>
    public string? Name { get; } = name;

    /// <summary>The name the contact set in their Signal profile.</summary>
    public string? ProfileName { get; } = profileName;

    /// <summary>The contact's Signal username.</summary>
    public string? Username { get; } = username;

    /// <summary>Whether the account blocked the contact.</summary>
    public bool IsBlocked { get; } = blocked;

    /// <summary>The best available display name: name, profile name, username, number, then id.</summary>
    public string DisplayName => Name ?? ProfileName ?? Username ?? Number ?? Id;
}

/// <summary>The safety-number (identity key) record of a contact, used to verify and trust keys.</summary>
/// <param name="id">The identity of the record (the contact's UUID, otherwise its phone number).</param>
/// <param name="number">The contact's phone number, if known.</param>
/// <param name="uuid">The contact's account identifier, if known.</param>
/// <param name="status">Trust level reported by signal-cli.</param>
/// <param name="fingerprint">The identity key fingerprint.</param>
/// <param name="safetyNumber">The safety number to compare out of band.</param>
/// <param name="added">When the key was first seen, as reported by signal-cli.</param>
public sealed class Identity(string id, string? number, Guid? uuid, string status, string? fingerprint, string? safetyNumber, string? added)
    : Entity<string>(id)
{
    /// <summary>The contact's phone number, if known.</summary>
    public string? Number { get; } = number;

    /// <summary>The contact's account identifier, if known.</summary>
    public Guid? Uuid { get; } = uuid;

    /// <summary>Trust level reported by signal-cli, e.g. <c>TRUSTED_VERIFIED</c>, <c>TRUSTED_UNVERIFIED</c>, <c>UNTRUSTED</c>.</summary>
    public string Status { get; } = status;

    /// <summary>The identity key fingerprint.</summary>
    public string? Fingerprint { get; } = fingerprint;

    /// <summary>The safety number to compare out of band.</summary>
    public string? SafetyNumber { get; } = safetyNumber;

    /// <summary>When the key was first seen, as reported by signal-cli.</summary>
    public string? Added { get; } = added;

    /// <summary>Whether the key is trusted (any <c>TRUSTED*</c> status).</summary>
    public bool IsTrusted => Status.StartsWith("TRUSTED", StringComparison.OrdinalIgnoreCase);
}
