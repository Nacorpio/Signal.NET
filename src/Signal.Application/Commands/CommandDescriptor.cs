using System.Reflection;
using System.Text;
using Signal.Application.Commands.Preconditions;

namespace Signal.Application.Commands;

/// <summary>A bindable parameter of a command.</summary>
/// <param name="Name">The parameter name, shown in usage and error messages.</param>
/// <param name="ParameterType">The CLR type the argument is converted to.</param>
/// <param name="IsOptional">Whether the argument may be omitted (has a default, is nullable, or is a switch).</param>
/// <param name="DefaultValue">The value used when the argument is omitted.</param>
/// <param name="IsRemainder">Whether the parameter takes the rest of the text (<see cref="RemainderAttribute"/>).</param>
/// <param name="FlagName">The option name for flag parameters (<see cref="FlagAttribute"/>); <see langword="null"/> for positional ones.</param>
/// <param name="Summary">The description from <see cref="SummaryAttribute"/>.</param>
public sealed record CommandParameter(
    string Name,
    Type ParameterType,
    bool IsOptional,
    object? DefaultValue,
    bool IsRemainder,
    string? FlagName,
    string? Summary)
{
    /// <summary>
    /// For collection parameters (<c>params T[]</c>, <c>T[]</c>, <c>List&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, …): the
    /// element type each remaining argument is converted to. <see langword="null"/> for single-value parameters.
    /// </summary>
    public Type? ElementType { get; init; }

    /// <summary>Whether the parameter collects all remaining positional arguments (see <see cref="ElementType"/>).</summary>
    public bool IsCollection => ElementType is not null;

    /// <summary>Whether the parameter is bound from a named option.</summary>
    public bool IsFlag => FlagName is not null;

    /// <summary>A boolean flag that does not take a value (<c>--verbose</c>).</summary>
    public bool IsSwitch => IsFlag && (ParameterType == typeof(bool) || ParameterType == typeof(bool?));

    /// <summary>
    /// Formats the parameter for usage lines: <c>&lt;a&gt;</c>, <c>[a]</c>, <c>&lt;text...&gt;</c>, <c>[numbers...]</c>,
    /// <c>[--flag &lt;v&gt;]</c>, <c>[--switch]</c>.
    /// </summary>
    /// <returns>The usage fragment.</returns>
    public override string ToString() => IsSwitch ? $"[--{FlagName}]"
        : IsFlag ? (IsOptional ? $"[--{FlagName} <{Name}>]" : $"--{FlagName} <{Name}>")
        : IsRemainder || IsCollection ? (IsOptional ? $"[{Name}...]" : $"<{Name}...>")
        : IsOptional ? $"[{Name}]" : $"<{Name}>";
}

/// <summary>The group a command belongs to (<see cref="CommandGroupAttribute"/>).</summary>
/// <param name="Name">The group name.</param>
/// <param name="Aliases">Alternative group names.</param>
/// <param name="Description">One-line description for help.</param>
public sealed record CommandGroupInfo(string Name, IReadOnlyList<string> Aliases, string? Description)
{
    /// <summary>Creates the group info from an attribute.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The group info.</returns>
    public static CommandGroupInfo From(CommandGroupAttribute attribute) =>
        new(attribute.Name, [.. attribute.Aliases], attribute.Description);
}

/// <summary>
/// Everything the framework knows about a command: metadata, parameters, preconditions and how to invoke it.
/// Created by the registry from <see cref="ICommand"/> classes and <see cref="CommandModule"/> methods, or directly
/// for delegate commands.
/// </summary>
public sealed class CommandDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="name">The command name (no whitespace).</param>
    /// <param name="executor">Invokes the command with the bound values (in <paramref name="parameters"/> order).</param>
    /// <param name="declaringType">The type that defines the command (used in diagnostics).</param>
    /// <param name="aliases">Alternative names.</param>
    /// <param name="description">One-line description for help.</param>
    /// <param name="usage">Custom usage text without prefix.</param>
    /// <param name="parameters">Bindable parameters.</param>
    /// <param name="preconditions">Checks to run before execution; sorted by <see cref="PreconditionAttribute.Order"/>.</param>
    /// <param name="hidden">Hide from the help listing.</param>
    /// <param name="method">The command method for module commands.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains whitespace.</exception>
    public CommandDescriptor(
        string name,
        Func<CommandContext, object?[], Task> executor,
        Type declaringType,
        IEnumerable<string>? aliases = null,
        string? description = null,
        string? usage = null,
        IEnumerable<CommandParameter>? parameters = null,
        IEnumerable<PreconditionAttribute>? preconditions = null,
        bool hidden = false,
        MethodInfo? method = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Command name '{name}' must not contain whitespace.", nameof(name));
        }

        Name = name;
        Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        DeclaringType = declaringType;
        Aliases = [.. aliases ?? []];
        Description = description;
        Usage = usage;
        Parameters = [.. parameters ?? []];
        Preconditions = [.. (preconditions ?? []).OrderBy(p => p.Order)];
        Hidden = hidden;
        Method = method;
    }

    /// <summary>The command name (within its <see cref="Group"/>, if any).</summary>
    public string Name { get; }

    /// <summary>The group the command belongs to, or <see langword="null"/> for a top-level command.</summary>
    public CommandGroupInfo? Group { get; init; }

    /// <summary>The name as typed after the prefix: <c>group name</c> for grouped commands, otherwise <see cref="Name"/>.</summary>
    public string FullName => Group is null ? Name : $"{Group.Name} {Name}";

    /// <summary>Alternative names.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>One-line description for help.</summary>
    public string? Description { get; }

    /// <summary>Custom usage text without prefix, or <see langword="null"/> to generate it.</summary>
    public string? Usage { get; }

    /// <summary>Bindable parameters in declaration order.</summary>
    public IReadOnlyList<CommandParameter> Parameters { get; }

    /// <summary>Preconditions in execution order.</summary>
    public IReadOnlyList<PreconditionAttribute> Preconditions { get; }

    /// <summary>Whether the command is hidden from the help listing.</summary>
    public bool Hidden { get; }

    /// <summary>The type that defines the command.</summary>
    public Type DeclaringType { get; }

    /// <summary>The command method for module commands; <see langword="null"/> otherwise.</summary>
    public MethodInfo? Method { get; }

    /// <summary>Invokes the command with the bound parameter values (in <see cref="Parameters"/> order).</summary>
    public Func<CommandContext, object?[], Task> Executor { get; }

    /// <summary>Formats the usage line, e.g. <c>/add &lt;a&gt; &lt;b&gt;</c> or <c>/playlist add &lt;song...&gt;</c>.</summary>
    /// <param name="prefix">The prefix to show (usually the one the user typed).</param>
    /// <returns>The usage line.</returns>
    public string FormatUsage(string prefix)
    {
        if (Usage is not null)
        {
            return prefix + Usage;
        }

        var builder = new StringBuilder(prefix).Append(FullName);
        foreach (var parameter in Parameters)
        {
            builder.Append(' ').Append(parameter);
        }

        return builder.ToString();
    }

    /// <summary>Returns <see cref="FullName"/>.</summary>
    /// <returns>The command name, including the group.</returns>
    public override string ToString() => FullName;
}
