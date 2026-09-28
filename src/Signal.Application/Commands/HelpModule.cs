using System.Text;

namespace Signal.Application.Commands;

/// <summary>
/// Built-in <c>help</c> command (aliases <c>?</c>, <c>commands</c>), generated from command metadata.
/// <c>/help</c> lists all visible commands; <c>/help &lt;command&gt;</c> shows usage, description, aliases and
/// parameter summaries. Disable with <c>Signal:Commands:EnableHelp = false</c>.
/// </summary>
/// <param name="registry">The command registry.</param>
public sealed class HelpModule(ICommandRegistry registry) : CommandModule
{
    /// <summary>Lists all commands or describes one.</summary>
    /// <param name="command">The command to describe (with or without prefix), or <see langword="null"/> to list all.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [Command("help", Aliases = ["?", "commands"], Description = "Lists all commands or shows details of one command.")]
    public Task HelpAsync([Summary("The command to describe")] string? command = null)
    {
        var prefix = Context.Parsed.Prefix;
        var builder = new StringBuilder();

        if (command is not null)
        {
            var name = command.StartsWith(prefix, StringComparison.Ordinal) ? command[prefix.Length..] : command;
            if (!registry.TryGetCommand(name, out var descriptor))
            {
                return ReplyAsync($"Unknown command '{command}'.");
            }

            builder.AppendLine(descriptor.FormatUsage(prefix));
            if (descriptor.Description is not null)
            {
                builder.AppendLine(descriptor.Description);
            }

            if (descriptor.Aliases.Count > 0)
            {
                builder.AppendLine($"Aliases: {string.Join(", ", descriptor.Aliases.Select(a => prefix + a))}");
            }

            foreach (var parameter in descriptor.Parameters.Where(p => p.Summary is not null))
            {
                builder.AppendLine($"  {(parameter.IsFlag ? "--" + parameter.FlagName : parameter.Name)}: {parameter.Summary}");
            }

            return ReplyAsync(builder.ToString().TrimEnd());
        }

        builder.AppendLine("Available commands:");
        foreach (var descriptor in registry.Commands.Where(c => !c.Hidden))
        {
            builder.Append(prefix).Append(descriptor.Name);
            if (descriptor.Description is not null)
            {
                builder.Append(" – ").Append(descriptor.Description);
            }

            builder.AppendLine();
        }

        builder.Append($"Send {prefix}help <command> for details.");
        return ReplyAsync(builder.ToString());
    }
}
