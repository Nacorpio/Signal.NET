using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;
using Signal.Application.Conversations;
using Signal.Application.Localization;

namespace Signal.Application.Commands;

/// <summary>
/// Built-in <c>help</c> command (aliases <c>?</c>, <c>commands</c>), generated from command metadata:
/// <list type="bullet">
/// <item><c>/help</c> lists all visible commands, under headings (command groups and <see cref="CategoryAttribute"/>)
/// when there is more than one, paged by <see cref="CommandOptions.HelpPageSize"/>;</item>
/// <item><c>/help 2</c> shows the second page;</item>
/// <item><c>/help &lt;command&gt;</c> shows usage, description, aliases, parameter summaries and examples;</item>
/// <item><c>/help &lt;group&gt;</c> lists a command group.</item>
/// </list>
/// Disable with <c>Signal:Commands:EnableHelp = false</c>.
/// </summary>
/// <param name="registry">The command registry.</param>
public sealed class HelpModule(ICommandRegistry registry) : CommandModule
{
    /// <summary>The heading for commands without a group or category, when headings are shown.</summary>
    private const string GeneralHeading = "General";

    /// <summary>Lists all commands (or one page of them), or describes one command or group.</summary>
    /// <param name="command">
    /// The command or group to describe (with or without prefix, e.g. <c>add</c> or <c>playlist add</c>), a page
    /// number, or <see langword="null"/> for the first page.
    /// </param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [Command("help", Aliases = ["?", "commands"], Description = "Lists all commands or shows details of one command.")]
    [Example("help"), Example("help 2"), Example("help add")]
    public async Task HelpAsync([Remainder, Summary("The command or group to describe, or a page number")] string? command = null)
    {
        var prefix = Context.Parsed.Prefix;

        // Commands disabled in this conversation are treated like hidden ones.
        var settings = await Context.Message.GetConversationSettingsAsync();
        bool Visible(CommandDescriptor c) => !c.Hidden && settings?.IsDisabled(c) != true;

        if (command is null)
        {
            await ReplyAsync(List(prefix, page: 1, Visible));
            return;
        }

        var trimmed = command.StartsWith(prefix, StringComparison.Ordinal) ? command[prefix.Length..] : command;
        var name = string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (registry.TryGetCommand(name, out var descriptor) && Visible(descriptor))
        {
            await ReplyAsync(Describe(descriptor, prefix));
            return;
        }

        var group = registry.GetGroup(name).Where(Visible).ToList();
        if (group.Count > 0)
        {
            await ReplyAsync(DescribeGroup(group, prefix));
            return;
        }

        // A number that isn't a command or group name is a page.
        await ReplyAsync(int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 0
            ? List(prefix, page, Visible)
            : Text(TextKey.HelpUnknown, command));
    }

    /// <summary>The command list, split into headed sections and pages.</summary>
    private string List(string prefix, int page, Func<CommandDescriptor, bool> visible)
    {
        var pageSize = Context.Services.GetRequiredService<IOptionsMonitor<SignalOptions>>().CurrentValue.Commands.HelpPageSize;

        // Ungrouped, uncategorised commands first, then groups and categories alphabetically.
        var sections = registry.Commands
            .Where(visible)
            .GroupBy(c => c.Group is { } g ? $"{prefix}{g.Name}{(g.Description is null ? null : " – " + g.Description)}" : c.Category ?? GeneralHeading)
            .OrderBy(s => s.Key == GeneralHeading ? 0 : 1)
            .ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entries = sections.SelectMany(s => s.Select(c => (Heading: s.Key, Command: c))).ToList();

        var pages = pageSize > 0 ? Math.Max(1, (entries.Count + pageSize - 1) / pageSize) : 1;
        if (page > pages)
        {
            return pages == 1 ? Text(TextKey.HelpOnlyOnePage) : Text(TextKey.HelpOnlyPages, pages);
        }

        var shown = pageSize > 0 ? entries.Skip((page - 1) * pageSize).Take(pageSize) : entries;
        var builder = new StringBuilder(pages > 1 ? Text(TextKey.HelpHeaderPaged, page, pages) : Text(TextKey.HelpHeader)).AppendLine();
        string? heading = null;
        foreach (var (entryHeading, descriptor) in shown)
        {
            // Headings only help when there is more than one section; small bots keep a flat list.
            if (sections.Count > 1 && entryHeading != heading)
            {
                builder.AppendLine().AppendLine(entryHeading == GeneralHeading ? Text(TextKey.HelpGeneral) : entryHeading);
                heading = entryHeading;
            }

            AppendEntry(builder, descriptor, prefix);
        }

        if (page < pages)
        {
            builder.AppendLine(Text(TextKey.HelpMore, prefix, page + 1));
        }

        return builder.Append(Text(TextKey.HelpDetails, prefix)).ToString();
    }

    private string DescribeGroup(List<CommandDescriptor> group, string prefix)
    {
        var info = group[0].Group!;
        var builder = new StringBuilder().AppendLine($"{prefix}{info.Name} <command>");
        if (info.Description is not null)
        {
            builder.AppendLine(info.Description);
        }

        if (info.Aliases.Count > 0)
        {
            builder.AppendLine(Text(TextKey.HelpAliases, string.Join(", ", info.Aliases.Select(a => prefix + a))));
        }

        foreach (var descriptor in group)
        {
            AppendEntry(builder, descriptor, prefix);
        }

        return builder.Append(Text(TextKey.HelpGroupDetails, prefix, info.Name)).ToString();
    }

    private string Describe(CommandDescriptor descriptor, string prefix)
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
            builder.AppendLine(Text(TextKey.HelpAliases, string.Join(", ", descriptor.Aliases.Select(a => prefix + group + a))));
        }

        foreach (var parameter in descriptor.Parameters.Where(p => p.Summary is not null))
        {
            builder.AppendLine($"  {(parameter.IsFlag ? "--" + parameter.FlagName : parameter.Name)}: {parameter.Summary}");
        }

        if (descriptor.Examples.Count > 0)
        {
            builder.AppendLine(Text(TextKey.HelpExamples));
            foreach (var example in descriptor.Examples)
            {
                builder.AppendLine($"  {prefix}{example}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>A text in the culture of the conversation.</summary>
    private string Text(string key, params object?[] args) => Context.Message.Text(key, args);

    private static void AppendEntry(StringBuilder builder, CommandDescriptor descriptor, string prefix)
    {
        builder.Append(prefix).Append(descriptor.FullName);
        if (descriptor.Description is not null)
        {
            builder.Append(" – ").Append(descriptor.Description);
        }

        builder.AppendLine();
    }
}
