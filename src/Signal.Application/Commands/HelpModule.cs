using System.Text;

namespace Signal.Application.Commands;

/// <summary>
/// Built-in <c>help</c> command (aliases <c>?</c>, <c>commands</c>), generated from command metadata.
/// <c>/help</c> lists all visible commands; <c>/help &lt;command&gt;</c> shows usage, description, aliases and
/// parameter summaries; <c>/help &lt;group&gt;</c> lists a command group. Disable with
/// <c>Signal:Commands:EnableHelp = false</c>.
/// </summary>
/// <param name="registry">The command registry.</param>
public sealed class HelpModule(ICommandRegistry registry) : CommandModule
{
    /// <summary>Lists all commands or describes one command or group.</summary>
    /// <param name="command">
    /// The command or group to describe (with or without prefix, e.g. <c>add</c> or <c>playlist add</c>), or
    /// <see langword="null"/> to list all.
    /// </param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [Command("help", Aliases = ["?", "commands"], Description = "Lists all commands or shows details of one command.")]
    public Task HelpAsync([Remainder, Summary("The command or group to describe")] string? command = null)
    {
        var prefix = Context.Parsed.Prefix;
        var builder = new StringBuilder();

        if (command is not null)
        {
            var trimmed = command.StartsWith(prefix, StringComparison.Ordinal) ? command[prefix.Length..] : command;
            var name = string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (registry.TryGetCommand(name, out var descriptor))
            {
                return ReplyAsync(Describe(descriptor, prefix));
            }

            var group = registry.GetGroup(name).Where(c => !c.Hidden).ToList();
            if (group.Count == 0)
            {
                return ReplyAsync($"Unknown command '{command}'.");
            }

            builder.AppendLine($"{prefix}{group[0].Group!.Name} <command>");
            if (group[0].Group!.Description is { } description)
            {
                builder.AppendLine(description);
            }

            if (group[0].Group!.Aliases.Count > 0)
            {
                builder.AppendLine($"Aliases: {string.Join(", ", group[0].Group!.Aliases.Select(a => prefix + a))}");
            }

            AppendList(builder, group, prefix);
            builder.Append($"Send {prefix}help {group[0].Group!.Name} <command> for details.");
            return ReplyAsync(builder.ToString());
        }

        builder.AppendLine("Available commands:");
        AppendList(builder, registry.Commands.Where(c => !c.Hidden), prefix);
        builder.Append($"Send {prefix}help <command> for details.");
        return ReplyAsync(builder.ToString());
    }

    private static string Describe(CommandDescriptor descriptor, string prefix)
    {
        var builder = new StringBuilder();
        builder.AppendLine(descriptor.FormatUsage(prefix));
        if (descriptor.Description is not null)
        {
            builder.AppendLine(descriptor.Description);
        }

        if (descriptor.Aliases.Count > 0)
        {
            var group = descriptor.Group is { } g ? g.Name + " " : string.Empty;
            builder.AppendLine($"Aliases: {string.Join(", ", descriptor.Aliases.Select(a => prefix + group + a))}");
        }

        foreach (var parameter in descriptor.Parameters.Where(p => p.Summary is not null))
        {
            builder.AppendLine($"  {(parameter.IsFlag ? "--" + parameter.FlagName : parameter.Name)}: {parameter.Summary}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, IEnumerable<CommandDescriptor> commands, string prefix)
    {
        foreach (var descriptor in commands)
        {
            builder.Append(prefix).Append(descriptor.FullName);
            if (descriptor.Description is not null)
            {
                builder.Append(" – ").Append(descriptor.Description);
            }

            builder.AppendLine();
        }
    }
}
