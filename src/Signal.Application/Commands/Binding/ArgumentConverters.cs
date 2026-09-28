using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Commands.Binding;

/// <summary>
/// Converts a raw argument into a parameter value. Register custom converters (as singletons) to support more
/// types or to override the built-in conversion of a type; the last registration for a type wins.
/// </summary>
public interface IArgumentConverter
{
    /// <summary>The type this converter produces.</summary>
    Type TargetType { get; }

    /// <summary>Human-readable description of the expected input, used in error messages ("'x' is not a valid …").</summary>
    string DisplayName => TargetType.Name;

    /// <summary>Converts the input.</summary>
    /// <param name="input">The raw argument text.</param>
    /// <param name="context">The command context (e.g. to resolve mentions or services).</param>
    /// <param name="value">The converted value when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the input is valid.</returns>
    bool TryConvert(string input, CommandContext context, out object? value);
}

/// <summary>Strongly typed base class for <see cref="IArgumentConverter"/>.</summary>
/// <typeparam name="T">The produced type.</typeparam>
/// <example>
/// <code>
/// public sealed class ColorConverter : ArgumentConverter&lt;Color&gt;
/// {
///     public override string DisplayName =&gt; "color (e.g. #ff8800)";
///     public override bool TryConvert(string input, CommandContext context, out Color value) =&gt; Color.TryParseHex(input, out value);
/// }
/// </code>
/// </example>
public abstract class ArgumentConverter<T> : IArgumentConverter
{
    /// <inheritdoc />
    public Type TargetType => typeof(T);

    /// <inheritdoc />
    public virtual string DisplayName => typeof(T).Name;

    /// <summary>Converts the input.</summary>
    /// <param name="input">The raw argument text.</param>
    /// <param name="context">The command context.</param>
    /// <param name="value">The converted value when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the input is valid.</returns>
    public abstract bool TryConvert(string input, CommandContext context, [MaybeNullWhen(false)] out T value);

    bool IArgumentConverter.TryConvert(string input, CommandContext context, out object? value)
    {
        if (TryConvert(input, context, out var result))
        {
            value = result;
            return true;
        }

        value = null;
        return false;
    }
}

/// <summary>Finds the converter for a parameter type.</summary>
public interface IArgumentConverterProvider
{
    /// <summary>Gets the converter for <paramref name="type"/>.</summary>
    /// <param name="type">The parameter type.</param>
    /// <param name="converter">The converter when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the type can be bound.</returns>
    bool TryGetConverter(Type type, [NotNullWhen(true)] out IArgumentConverter? converter);
}

/// <summary>Passes text through unchanged.</summary>
internal sealed class StringArgumentConverter : ArgumentConverter<string>
{
    public override string DisplayName => "text";

    public override bool TryConvert(string input, CommandContext context, [MaybeNullWhen(false)] out string value)
    {
        value = input;
        return true;
    }
}

/// <summary>Accepts <c>true/false</c>, <c>yes/no</c>, <c>y/n</c>, <c>on/off</c> and <c>1/0</c> (case-insensitive).</summary>
internal sealed class BooleanArgumentConverter : ArgumentConverter<bool>
{
    private static readonly HashSet<string> True = new(["true", "yes", "y", "on", "1"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> False = new(["false", "no", "n", "off", "0"], StringComparer.OrdinalIgnoreCase);

    public override string DisplayName => "yes/no";

    public override bool TryConvert(string input, CommandContext context, out bool value)
    {
        value = True.Contains(input);
        return value || False.Contains(input);
    }
}

/// <summary>Converts via <see cref="Recipient.TryParse"/> (group id, phone number, UUID or username).</summary>
internal sealed class RecipientArgumentConverter : ArgumentConverter<Recipient>
{
    public override string DisplayName => "recipient (phone number, UUID, username or group id)";

    public override bool TryConvert(string input, CommandContext context, out Recipient value) =>
        Recipient.TryParse(input, out value);
}

/// <summary>
/// Resolves converters: explicitly registered ones first, then enums and any <see cref="IParsable{TSelf}"/> type
/// (numbers, <see cref="Guid"/>, <see cref="TimeSpan"/>, <see cref="PhoneNumber"/>, <see cref="GroupId"/>, …) using the
/// invariant culture. <see cref="Nullable{T}"/> is unwrapped. Results are cached per type.
/// </summary>
internal sealed class ArgumentConverterProvider(IEnumerable<IArgumentConverter> converters) : IArgumentConverterProvider
{
    private readonly Dictionary<Type, IArgumentConverter> _registered = converters
        .GroupBy(c => c.TargetType)
        .ToDictionary(g => g.Key, g => g.Last());

    private readonly ConcurrentDictionary<Type, IArgumentConverter?> _cache = new();

    public bool TryGetConverter(Type type, [NotNullWhen(true)] out IArgumentConverter? converter)
    {
        converter = _cache.GetOrAdd(type, Create);
        return converter is not null;
    }

    private IArgumentConverter? Create(Type type)
    {
        if (_registered.TryGetValue(type, out var registered))
        {
            return registered;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TryGetConverter(underlying, out var inner) ? inner : null;
        }

        if (type.IsEnum)
        {
            return new EnumArgumentConverter(type);
        }

        var isParsable = type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>) && i.GenericTypeArguments[0] == type);
        return isParsable ? new ParsableArgumentConverter(type) : null;
    }

    /// <summary>Parses enum member names case-insensitively; numeric input is rejected to avoid undefined values.</summary>
    private sealed class EnumArgumentConverter(Type enumType) : IArgumentConverter
    {
        public Type TargetType => enumType;

        public string DisplayName => $"one of: {string.Join(", ", Enum.GetNames(enumType)).ToLowerInvariant()}";

        public bool TryConvert(string input, CommandContext context, out object? value)
        {
            value = null;
            return !char.IsAsciiDigit(input.FirstOrDefault()) && Enum.TryParse(enumType, input, ignoreCase: true, out value);
        }
    }

    /// <summary>Calls the static <c>TryParse</c> of <see cref="IParsable{TSelf}"/> through a delegate created once per type.</summary>
    private sealed class ParsableArgumentConverter : IArgumentConverter
    {
        private delegate bool Parser(string input, out object? value);

        private static readonly MethodInfo ParseMethod =
            typeof(ParsableArgumentConverter).GetMethod(nameof(Parse), BindingFlags.NonPublic | BindingFlags.Static)!;

        private readonly Parser _parser;

        public ParsableArgumentConverter(Type type)
        {
            TargetType = type;
            _parser = ParseMethod.MakeGenericMethod(type).CreateDelegate<Parser>();
        }

        public Type TargetType { get; }

        public string DisplayName => TargetType switch
        {
            _ when TargetType == typeof(int) || TargetType == typeof(long) || TargetType == typeof(short) => "whole number",
            _ when TargetType == typeof(double) || TargetType == typeof(decimal) || TargetType == typeof(float) => "number",
            _ when TargetType == typeof(TimeSpan) => "duration (e.g. 00:05:00)",
            _ when TargetType == typeof(PhoneNumber) => "phone number (e.g. +4915112345678)",
            _ => TargetType.Name,
        };

        public bool TryConvert(string input, CommandContext context, out object? value) => _parser(input, out value);

        private static bool Parse<T>(string input, out object? value)
            where T : IParsable<T>
        {
            if (T.TryParse(input, CultureInfo.InvariantCulture, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }
}
