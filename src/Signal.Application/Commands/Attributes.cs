namespace Signal.Application.Commands;

/// <summary>
/// Declares a command: on a public method of a <see cref="CommandModule"/>, or on an <see cref="ICommand"/> class
/// deriving from <see cref="CommandBase"/> (which then reads its metadata from this attribute).
/// </summary>
/// <param name="name">The command name, typed after the prefix (e.g. <c>ping</c> for <c>/ping</c>). Must not contain whitespace.</param>
/// <example>
/// <code>
/// [Command("roll", Aliases = ["dice"], Description = "Rolls a die")]
/// public Task RollAsync(int sides = 6) => ReplyAsync($"{Random.Shared.Next(1, sides + 1)}");
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute(string name) : Attribute
{
    /// <summary>The command name.</summary>
    public string Name { get; } = name;

    /// <summary>Alternative names. Names and aliases must be unique across all commands.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>One-line description shown by the <c>help</c> command.</summary>
    public string? Description { get; set; }

    /// <summary>Custom usage text (without prefix). Generated from the parameters when omitted.</summary>
    public string? Usage { get; set; }

    /// <summary>Hide the command from the help listing (it can still be executed and described).</summary>
    public bool Hidden { get; set; }
}

/// <summary>
/// Binds all remaining text to this <see cref="string"/> parameter, verbatim (whitespace preserved).
/// Must be the last positional parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class RemainderAttribute : Attribute;

/// <summary>
/// An example invocation shown by <c>/help &lt;command&gt;</c>, written without prefix (e.g. <c>add 2 3</c>).
/// Repeat the attribute for several examples.
/// </summary>
/// <param name="text">The example, without prefix.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ExampleAttribute(string text) : Attribute
{
    /// <summary>The example, without prefix.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// Lists commands under a heading in <c>/help</c>. On a module it applies to all of its commands; on a method it
/// overrides the module's category. Command groups get their own heading and ignore categories.
/// </summary>
/// <param name="name">The heading, e.g. <c>Moderation</c>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CategoryAttribute(string name) : Attribute
{
    /// <summary>The heading.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Puts every command of a <see cref="CommandModule"/> (or an <see cref="ICommand"/> class) under a group, so it is
/// invoked as <c>/group command</c>. Preconditions on the module apply to all of its commands.
/// </summary>
/// <example>
/// <code>
/// [CommandGroup("playlist", Aliases = ["pl"], Description = "Manage the playlist"), RequireGroup]
/// public sealed class PlaylistModule : CommandModule
/// {
///     [Command("add")]    public Task AddAsync([Remainder] string song) =&gt; …;   // /playlist add …, /pl add …
///     [Command("remove")] public Task RemoveAsync(int index) =&gt; …;           // /playlist remove 2
/// }
/// </code>
/// </example>
/// <param name="name">The group name (no whitespace).</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandGroupAttribute(string name) : Attribute
{
    /// <summary>The group name.</summary>
    public string Name { get; } = name;

    /// <summary>Alternative group names.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>One-line description for help.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Binds the parameter to a named option (<c>--name value</c> or <c>--name=value</c>) instead of a position.
/// <see cref="bool"/> flags are switches: <c>--name</c> sets them to <see langword="true"/>.
/// </summary>
/// <param name="name">The option name; defaults to the parameter name.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FlagAttribute(string? name = null) : Attribute
{
    /// <summary>The option name, or <see langword="null"/> to use the parameter name.</summary>
    public string? Name { get; } = name;
}

/// <summary>Describes a parameter in the output of <c>help &lt;command&gt;</c>.</summary>
/// <param name="text">The description.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SummaryAttribute(string text) : Attribute
{
    /// <summary>The description.</summary>
    public string Text { get; } = text;
}
