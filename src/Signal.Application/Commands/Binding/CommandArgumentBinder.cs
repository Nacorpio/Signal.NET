using Signal.Application.Commands.Parsing;

namespace Signal.Application.Commands.Binding;

/// <summary>All parameters were bound.</summary>
/// <param name="Values">The bound values in <see cref="CommandDescriptor.Parameters"/> order.</param>
public sealed record BoundArguments(object?[] Values);

/// <summary>The arguments could not be bound.</summary>
/// <param name="Message">A user-facing error, e.g. <c>Missing argument &lt;b&gt;.</c></param>
public sealed record ArgumentBindingError(string Message);

/// <summary>
/// Outcome of argument binding: either <see cref="BoundArguments"/> or an <see cref="ArgumentBindingError"/>.
/// </summary>
/// <remarks>
/// Consumers must handle both cases; there is no way to read values from a failed binding:
/// <code>
/// switch (binder.Bind(context))
/// {
///     case ArgumentBindingError error: /* reply with error.Message */ break;
///     case BoundArguments bound: /* invoke with bound.Values */ break;
/// }
/// </code>
/// </remarks>
public union ArgumentBindingResult(BoundArguments, ArgumentBindingError);

/// <summary>Binds parsed tokens to a command's declared parameters.</summary>
public interface ICommandArgumentBinder
{
    /// <summary>Binds the arguments of <see cref="CommandContext.Parsed"/> to <see cref="CommandContext.Command"/>'s parameters.</summary>
    /// <param name="context">The command context.</param>
    /// <returns>The bound values, or a user-facing error.</returns>
    /// <exception cref="InvalidOperationException">A parameter type has no converter (a programming error, not a user error).</exception>
    ArgumentBindingResult Bind(CommandContext context);
}

/// <summary>
/// Default binder. Rules:
/// <list type="bullet">
/// <item>Flag parameters bind from <c>--name</c> options; boolean flags are switches.</item>
/// <item>Positional parameters bind in order; missing optional ones get their default.</item>
/// <item>A <c>[Remainder]</c> parameter takes the rest of the text verbatim.</item>
/// <item>Extra positional arguments and unknown flags are errors.</item>
/// </list>
/// Commands without declared parameters (e.g. <see cref="ICommand"/> classes) always bind successfully.
/// </summary>
internal sealed class CommandArgumentBinder(IArgumentConverterProvider converters) : ICommandArgumentBinder
{
    public ArgumentBindingResult Bind(CommandContext context)
    {
        var parameters = context.Command.Parameters;
        if (parameters.Count == 0)
        {
            // Class-based commands (or parameterless methods) read Arguments/Flags themselves.
            return new BoundArguments([]);
        }

        var switches = parameters.Where(p => p.IsSwitch).Select(p => p.FlagName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var split = context.Parsed.Split(switches);
        var unusedFlags = new HashSet<string>(split.Flags.Keys, StringComparer.OrdinalIgnoreCase);
        var values = new object?[parameters.Count];
        var position = 0;

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            string? error;

            if (parameter.IsFlag)
            {
                var name = parameter.FlagName!;
                unusedFlags.Remove(name);
                if (!split.Flags.TryGetValue(name, out var raw))
                {
                    if (!parameter.IsOptional)
                    {
                        return new ArgumentBindingError($"Missing required option --{name}.");
                    }

                    values[i] = parameter.DefaultValue;
                    continue;
                }

                if (raw is null)
                {
                    if (!parameter.IsSwitch)
                    {
                        return new ArgumentBindingError($"Option --{name} requires a value.");
                    }

                    values[i] = true;
                    continue;
                }

                if ((error = Convert(parameter, raw, context, out values[i])) is not null)
                {
                    return new ArgumentBindingError(error);
                }

                continue;
            }

            if (position >= split.Positional.Count)
            {
                if (!parameter.IsOptional)
                {
                    return new ArgumentBindingError($"Missing argument <{parameter.Name}>.");
                }

                values[i] = parameter.DefaultValue;
                continue;
            }

            var input = parameter.IsRemainder ? Remainder(context, split, position) : split.Positional[position].Value;
            position = parameter.IsRemainder ? split.Positional.Count : position + 1;

            if ((error = Convert(parameter, input, context, out values[i])) is not null)
            {
                return new ArgumentBindingError(error);
            }
        }

        if (position < split.Positional.Count)
        {
            return new ArgumentBindingError($"Too many arguments ('{split.Positional[position].Value}' was not expected).");
        }

        if (unusedFlags.Count > 0)
        {
            return new ArgumentBindingError($"Unknown option --{unusedFlags.First()}.");
        }

        return new BoundArguments(values);
    }

    /// <summary>
    /// The verbatim rest of the text, unless flags follow (then the remaining token values are joined with spaces).
    /// A single quoted token yields its unquoted value.
    /// </summary>
    private static string Remainder(CommandContext context, CommandArguments split, int position)
    {
        var first = split.Positional[position];
        if (position == split.Positional.Count - 1 && first.IsQuoted)
        {
            return first.Value;
        }

        return split.LastFlagStart < first.Start
            ? context.Parsed.RawArguments[first.Start..]
            : string.Join(' ', split.Positional.Skip(position).Select(t => t.Value));
    }

    /// <summary>Converts one value; returns a user-facing error or <see langword="null"/> on success.</summary>
    private string? Convert(CommandParameter parameter, string input, CommandContext context, out object? value)
    {
        if (!converters.TryGetConverter(parameter.ParameterType, out var converter))
        {
            throw new InvalidOperationException(
                $"No argument converter registered for {parameter.ParameterType} (parameter '{parameter.Name}' of command '{context.Command.Name}').");
        }

        return converter.TryConvert(input, context, out value)
            ? null
            : $"'{input}' is not a valid {converter.DisplayName} for <{parameter.Name}>.";
    }
}
