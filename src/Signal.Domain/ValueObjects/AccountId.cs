using System.Diagnostics.CodeAnalysis;

namespace Signal.Domain.ValueObjects;

/// <summary>
/// A Signal account identifier (ACI), a UUID that identifies an account independently of its phone number.
/// Used to address users who hide their phone number.
/// </summary>
/// <param name="Value">The UUID.</param>
public readonly record struct AccountId(Guid Value) : IParsable<AccountId>
{
    /// <summary>Parses a UUID in any format accepted by <see cref="Guid.Parse(string)"/>.</summary>
    /// <param name="value">The UUID text.</param>
    /// <returns>The account id.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a UUID.</exception>
    public static AccountId Parse(string value) => new(Guid.Parse(value));

    /// <summary>Tries to parse a UUID.</summary>
    /// <param name="value">The UUID text.</param>
    /// <param name="id">The account id when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a UUID.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out AccountId id)
    {
        var success = Guid.TryParse(value, out var guid);
        id = new AccountId(guid);
        return success;
    }

    static AccountId IParsable<AccountId>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<AccountId>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AccountId result) =>
        TryParse(s, out result);

    /// <summary>Returns the UUID in the hyphenated lower-case form used by the API.</summary>
    /// <returns>E.g. <c>8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f</c>.</returns>
    public override string ToString() => Value.ToString("D");
}
