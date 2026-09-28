using System.Diagnostics.CodeAnalysis;
using Signal.Domain.Exceptions;

namespace Signal.Domain.ValueObjects;

/// <summary>A Signal username, e.g. <c>alice.42</c>. Never empty and never contains whitespace.</summary>
public readonly record struct Username : IParsable<Username>
{
    private Username(string value) => Value = value;

    /// <summary>The username.</summary>
    public string Value { get; }

    /// <summary>Parses a username (surrounding whitespace is trimmed).</summary>
    /// <param name="value">The username text.</param>
    /// <returns>The username.</returns>
    /// <exception cref="InvalidRecipientException"><paramref name="value"/> is empty or contains whitespace.</exception>
    public static Username Parse(string value) =>
        TryParse(value, out var username)
            ? username
            : throw new InvalidRecipientException(value, "usernames must not be empty or contain whitespace.");

    /// <summary>Tries to parse a username.</summary>
    /// <param name="value">The username text.</param>
    /// <param name="username">The username when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid username.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out Username username)
    {
        username = default;
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        username = new Username(trimmed);
        return true;
    }

    static Username IParsable<Username>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<Username>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Username result) =>
        TryParse(s, out result);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The username, or an empty string for <see langword="default"/>.</returns>
    public override string ToString() => Value ?? string.Empty;
}
