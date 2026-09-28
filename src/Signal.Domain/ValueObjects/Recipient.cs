using System.Diagnostics.CodeAnalysis;
using Signal.Domain.Exceptions;

namespace Signal.Domain.ValueObjects;

/// <summary>
/// Anything a message can be addressed to: exactly one of a <see cref="PhoneNumber"/>, an <see cref="AccountId"/>
/// (ACI/UUID), a <see cref="Username"/> or a <see cref="GroupId"/>.
/// </summary>
/// <remarks>
/// <para>
/// A C# union: every case type converts implicitly (<c>Recipient r = phoneNumber;</c>), and pattern matching applies
/// to the contained value, with exhaustiveness checking:
/// </para>
/// <code>
/// var label = recipient switch
/// {
///     PhoneNumber n =&gt; $"phone {n}",
///     AccountId id =&gt; $"account {id}",
///     Username u =&gt; $"@{u}",
///     GroupId g =&gt; $"group {g}",
/// };
/// </code>
/// <para><see cref="Address"/> is the string sent to the REST API.</para>
/// </remarks>
public union Recipient(PhoneNumber, AccountId, Username, GroupId)
{
    /// <summary>The identifier in the form expected by the REST API.</summary>
    /// <exception cref="InvalidOperationException">The recipient is <see langword="default"/> (holds no value).</exception>
    public string Address => this switch
    {
        PhoneNumber number => number.Value,
        AccountId id => id.ToString(),
        Username username => username.Value,
        GroupId group => group.Value,
        null => throw new InvalidOperationException("The recipient has no value."),
    };

    /// <summary>Whether this recipient is a group.</summary>
    public bool IsGroup => this is GroupId;

    /// <summary>
    /// Detects the recipient kind from its textual form. Tried in order: group id (<c>group.…</c>),
    /// phone number, UUID; anything else is treated as a username.
    /// </summary>
    /// <param name="value">The text to interpret.</param>
    /// <returns>The recipient.</returns>
    /// <exception cref="InvalidRecipientException"><paramref name="value"/> is empty or an invalid username.</exception>
    public static Recipient Parse(string value) =>
        TryParse(value, out var recipient)
            ? recipient
            : throw new InvalidRecipientException(value, "expected a phone number, UUID, username or group id.");

    /// <summary>Tries to detect the recipient kind from its textual form (see <see cref="Parse"/>).</summary>
    /// <param name="value">The text to interpret.</param>
    /// <param name="recipient">The recipient when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid recipient.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out Recipient recipient)
    {
        recipient = default;
        if (GroupId.TryParse(value, out var group))
        {
            recipient = group;
        }
        else if (PhoneNumber.TryParse(value, out var number))
        {
            recipient = number;
        }
        else if (AccountId.TryParse(value, out var id))
        {
            recipient = id;
        }
        else if (Username.TryParse(value, out var username))
        {
            recipient = username;
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>Returns <see cref="Address"/>.</summary>
    /// <returns>The API representation of this recipient, or an empty string for <see langword="default"/>.</returns>
    public override string ToString() => Value is null ? string.Empty : Address;
}
