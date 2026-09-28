using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Signal.Domain.Exceptions;

namespace Signal.Domain.ValueObjects;

/// <summary>
/// A phone number in E.164 format (<c>+</c> followed by 7–15 digits, e.g. <c>+4915112345678</c>).
/// </summary>
/// <remarks>
/// Instances are always valid: they can only be created through <see cref="Parse"/> or <see cref="TryParse(string?, out PhoneNumber)"/>,
/// which normalise common formatting (spaces, dashes, dots, parentheses and a leading <c>00</c>).
/// Implements <see cref="IParsable{TSelf}"/>, so command parameters of this type are bound automatically.
/// </remarks>
public readonly partial record struct PhoneNumber : IParsable<PhoneNumber>
{
    private PhoneNumber(string value) => Value = value;

    /// <summary>The normalised E.164 representation, e.g. <c>+4915112345678</c>.</summary>
    public string Value { get; }

    /// <summary>Parses and normalises a phone number.</summary>
    /// <param name="value">Input such as <c>+49 151 1234-5678</c> or <c>004915112345678</c>.</param>
    /// <returns>The normalised phone number.</returns>
    /// <exception cref="InvalidPhoneNumberException"><paramref name="value"/> is not a valid phone number.</exception>
    public static PhoneNumber Parse(string value) =>
        TryParse(value, out var number) ? number : throw new InvalidPhoneNumberException(value);

    /// <summary>Tries to parse and normalise a phone number.</summary>
    /// <param name="value">Input such as <c>+49 151 1234-5678</c> or <c>004915112345678</c>.</param>
    /// <param name="number">The normalised phone number when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid phone number.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out PhoneNumber number)
    {
        number = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Tolerate common formatting characters such as spaces, dashes, dots and parentheses.
        var normalized = Separators().Replace(value.Trim(), string.Empty);
        if (normalized.StartsWith("00", StringComparison.Ordinal))
        {
            normalized = "+" + normalized[2..];
        }

        if (!E164().IsMatch(normalized))
        {
            return false;
        }

        number = new PhoneNumber(normalized);
        return true;
    }

    static PhoneNumber IParsable<PhoneNumber>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<PhoneNumber>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PhoneNumber result) =>
        TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The E.164 representation, or an empty string for <see langword="default"/>.</returns>
    public override string ToString() => Value ?? string.Empty;

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();

    [GeneratedRegex(@"[\s\-\.\(\)]", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();
}
