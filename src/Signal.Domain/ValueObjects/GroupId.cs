using System.Diagnostics.CodeAnalysis;
using System.Text;
using Signal.Domain.Exceptions;

namespace Signal.Domain.ValueObjects;

/// <summary>
/// A group identifier in the form used by the REST API: <c>group.</c> followed by the base64 encoded signal-cli internal id.
/// </summary>
/// <remarks>
/// Received envelopes carry the <em>internal</em> id, while REST endpoints and <c>/v2/send</c> recipients expect the
/// <c>group.…</c> form. Use <see cref="FromInternalId"/> and <see cref="InternalId"/> to convert between the two.
/// </remarks>
public readonly record struct GroupId : IParsable<GroupId>
{
    /// <summary>The prefix of every REST API group id.</summary>
    public const string Prefix = "group.";

    private GroupId(string value) => Value = value;

    /// <summary>The REST API form, e.g. <c>group.ZWtKTm...</c>.</summary>
    public string Value { get; }

    /// <summary>The signal-cli internal id (the form used inside received envelopes).</summary>
    /// <exception cref="FormatException">The part after <see cref="Prefix"/> is not valid base64.</exception>
    public string InternalId => Encoding.UTF8.GetString(Convert.FromBase64String(Value[Prefix.Length..]));

    /// <summary>Parses a REST API group id.</summary>
    /// <param name="value">A value starting with <see cref="Prefix"/>.</param>
    /// <returns>The group id.</returns>
    /// <exception cref="InvalidRecipientException"><paramref name="value"/> does not start with <see cref="Prefix"/>.</exception>
    public static GroupId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new InvalidRecipientException(value, $"group ids must start with '{Prefix}'.");

    /// <summary>Tries to parse a REST API group id.</summary>
    /// <param name="value">A value starting with <see cref="Prefix"/>.</param>
    /// <param name="id">The group id when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a group id.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out GroupId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) || value.Length == Prefix.Length)
        {
            return false;
        }

        id = new GroupId(value);
        return true;
    }

    /// <summary>Creates the REST API id from the signal-cli internal id found in received messages.</summary>
    /// <param name="internalId">The internal id, e.g. the <c>groupInfo.groupId</c> of an envelope.</param>
    /// <returns>The REST API group id (<c>group.</c> + base64 of <paramref name="internalId"/>).</returns>
    public static GroupId FromInternalId(string internalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalId);
        return new GroupId(Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(internalId)));
    }

    static GroupId IParsable<GroupId>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<GroupId>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out GroupId result) =>
        TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The REST API form, or an empty string for <see langword="default"/>.</returns>
    public override string ToString() => Value ?? string.Empty;
}
