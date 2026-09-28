using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;

namespace Signal.Application.Commands.Parsing;

/// <summary>A token of the argument text.</summary>
/// <param name="Value">The token text, with surrounding quotes removed and escapes resolved.</param>
/// <param name="IsQuoted">Whether the token was quoted (quoted tokens are never flags).</param>
/// <param name="Start">Offset of the token in <see cref="ParsedCommand.RawArguments"/>.</param>
public readonly record struct CommandToken(string Value, bool IsQuoted, int Start)
{
    /// <summary>Whether the token looks like a flag (<c>--name</c>, unquoted).</summary>
    public bool IsFlag => !IsQuoted && Value.Length > 2 && Value.StartsWith("--", StringComparison.Ordinal);

    /// <summary>Whether the token is the end-of-flags marker <c>--</c>.</summary>
    public bool IsEndOfFlags => !IsQuoted && Value == "--";
}

/// <summary>Positional arguments and flags of a command, split with knowledge of which flags are switches.</summary>
/// <param name="Positional">Positional tokens in order.</param>
/// <param name="Flags">Flag values by name (case-insensitive); <see langword="null"/> for flags without a value.</param>
/// <param name="LastFlagStart">Offset of the last flag token, or -1 if there is none.</param>
public sealed record CommandArguments(IReadOnlyList<CommandToken> Positional, IReadOnlyDictionary<string, string?> Flags, int LastFlagStart);

/// <summary>A command invocation such as <c>/remind --in 00:10:00 "buy milk"</c>.</summary>
public sealed class ParsedCommand
{
    private CommandArguments? _naive;

    /// <summary>Creates a parsed command and tokenizes <paramref name="rawArguments"/>.</summary>
    /// <param name="prefix">The matched prefix.</param>
    /// <param name="name">The command name as typed.</param>
    /// <param name="rawArguments">Everything after the name, trimmed.</param>
    public ParsedCommand(string prefix, string name, string rawArguments)
    {
        Prefix = prefix;
        Name = name;
        RawArguments = rawArguments;
        Tokens = CommandTokenizer.Tokenize(rawArguments);
    }

    /// <summary>The matched prefix, e.g. <c>/</c>.</summary>
    public string Prefix { get; }

    /// <summary>The command name as typed (may be an alias).</summary>
    public string Name { get; }

    /// <summary>Everything after the command name, trimmed.</summary>
    public string RawArguments { get; }

    /// <summary>The tokens of <see cref="RawArguments"/>.</summary>
    public IReadOnlyList<CommandToken> Tokens { get; }

    /// <summary>Positional arguments using the naive split (see <see cref="Split"/> with no switches).</summary>
    public IReadOnlyList<string> Arguments => [.. (_naive ??= Split(null)).Positional.Select(t => t.Value)];

    /// <summary>Flags using the naive split (see <see cref="Split"/> with no switches).</summary>
    public IReadOnlyDictionary<string, string?> Flags => (_naive ??= Split(null)).Flags;

    /// <summary>
    /// Splits tokens into positional arguments and flags. A flag consumes the following token as its value
    /// unless it is listed in <paramref name="switches"/>, uses <c>--name=value</c>, or is followed by another flag.
    /// A bare <c>--</c> ends flag parsing; later tokens are positional even if they start with <c>--</c>.
    /// </summary>
    /// <param name="switches">Flag names that never take a value (boolean flags), or <see langword="null"/>.</param>
    /// <returns>The split arguments.</returns>
    public CommandArguments Split(IReadOnlySet<string>? switches)
    {
        var positional = new List<CommandToken>();
        var flags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var lastFlagStart = -1;
        var flagsEnded = false;

        for (var i = 0; i < Tokens.Count; i++)
        {
            var token = Tokens[i];
            if (!flagsEnded && token.IsEndOfFlags)
            {
                flagsEnded = true;
                continue;
            }

            if (flagsEnded || !token.IsFlag)
            {
                positional.Add(token);
                continue;
            }

            lastFlagStart = token.Start;
            var body = token.Value[2..];
            var equals = body.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                flags[body[..equals]] = body[(equals + 1)..];
            }
            else if (switches?.Contains(body) != true && i + 1 < Tokens.Count && !Tokens[i + 1].IsFlag && !Tokens[i + 1].IsEndOfFlags)
            {
                flags[body] = Tokens[++i].Value;
            }
            else
            {
                flags[body] = null;
            }
        }

        return new CommandArguments(positional, flags, lastFlagStart);
    }

    /// <summary>Formats the invocation, e.g. <c>/add 1 2</c>.</summary>
    /// <returns>The prefix, name and raw arguments.</returns>
    public override string ToString() => $"{Prefix}{Name} {RawArguments}".TrimEnd();
}

/// <summary>Splits argument text into <see cref="CommandToken"/>s.</summary>
public static class CommandTokenizer
{
    /// <summary>
    /// Splits on whitespace. Tokens starting with a quote (<c>"</c>, <c>'</c>, or the typographic quotes
    /// <c>“ ”</c>, <c>„ “</c>, <c>« »</c> inserted by mobile keyboards) extend to the closing quote; inside,
    /// a backslash escapes the closing quote or a backslash. Quotes inside a word (<c>don't</c>) are literal.
    /// An unterminated quote extends to the end of the input.
    /// </summary>
    /// <param name="input">The argument text.</param>
    /// <returns>The tokens in order.</returns>
    public static IReadOnlyList<CommandToken> Tokenize(string input)
    {
        var tokens = new List<CommandToken>();
        var current = new StringBuilder();
        char? closingQuote = null;
        var inToken = false;
        var quoted = false;
        var start = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (closingQuote is { } closing)
            {
                if (c == '\\' && i + 1 < input.Length && (input[i + 1] == closing || input[i + 1] == '\\'))
                {
                    current.Append(input[++i]);
                }
                else if (c == closing)
                {
                    closingQuote = null;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(new CommandToken(current.ToString(), quoted, start));
                    current.Clear();
                    inToken = quoted = false;
                }

                continue;
            }

            if (!inToken)
            {
                inToken = true;
                start = i;
                if (GetClosingQuote(c) is { } q)
                {
                    closingQuote = q;
                    quoted = true;
                    continue;
                }
            }

            current.Append(c);
        }

        if (inToken)
        {
            tokens.Add(new CommandToken(current.ToString(), quoted, start));
        }

        return tokens;
    }

    private static char? GetClosingQuote(char c) => c switch
    {
        '"' => '"',
        '\'' => '\'',
        '“' => '”', // “ ”
        '„' => '“', // „ “
        '«' => '»', // « »
        _ => null,
    };
}

/// <summary>Recognises command invocations in message text.</summary>
public interface ICommandParser
{
    /// <summary>Parses <paramref name="text"/> if it starts with a configured prefix directly followed by a command name.</summary>
    /// <param name="text">The message text.</param>
    /// <param name="command">The parsed invocation when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the text is a command invocation.</returns>
    bool TryParse(string? text, [NotNullWhen(true)] out ParsedCommand? command);
}

/// <summary>
/// Default parser using <see cref="CommandOptions.EffectivePrefixes"/> (longest match wins). Leading whitespace
/// is ignored; a prefix followed by whitespace (<c>/ ping</c>) is not a command.
/// </summary>
internal sealed class CommandParser(IOptionsMonitor<SignalOptions> options) : ICommandParser
{
    public bool TryParse(string? text, [NotNullWhen(true)] out ParsedCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.TrimStart();
        var prefix = options.CurrentValue.Commands.EffectivePrefixes
            .Where(p => text.StartsWith(p, StringComparison.Ordinal))
            .MaxBy(p => p.Length);
        if (prefix is null)
        {
            return false;
        }

        var rest = text.AsSpan(prefix.Length);
        if (rest.IsEmpty || char.IsWhiteSpace(rest[0]))
        {
            return false;
        }

        var nameEnd = 0;
        while (nameEnd < rest.Length && !char.IsWhiteSpace(rest[nameEnd]))
        {
            nameEnd++;
        }

        command = new ParsedCommand(prefix, rest[..nameEnd].ToString(), rest[nameEnd..].Trim().ToString());
        return true;
    }
}
