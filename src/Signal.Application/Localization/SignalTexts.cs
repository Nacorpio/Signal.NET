using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;
using Signal.Application.Conversations;
using Signal.Application.Pipeline;

namespace Signal.Application.Localization;

/// <summary>
/// Keys of the texts the framework sends (errors, help, prompts). Translate them under
/// <c>Signal:Localization:Texts:{culture}:{key}</c>; <c>{0}</c>, <c>{1}</c>, … are the documented arguments.
/// Argument type names in error messages are translated with the key <c>Type.{English display name}</c>,
/// e.g. <c>Type.whole number</c>.
/// </summary>
public static class TextKey
{
    /// <summary>Unknown command. {0} = typed name, {1} = prefix. Default: <c>Commands:UnknownCommandMessage</c>.</summary>
    public const string UnknownCommand = nameof(UnknownCommand);

    /// <summary>Command disabled in the conversation. Default: <c>Commands:DisabledCommandMessage</c>; empty = no reply.</summary>
    public const string DisabledCommand = nameof(DisabledCommand);

    /// <summary>A command threw. Default: <c>Commands:ErrorMessage</c>.</summary>
    public const string CommandError = nameof(CommandError);

    /// <summary>Appended suggestion. {0} = suggested invocation, e.g. <c>/help</c>.</summary>
    public const string DidYouMean = nameof(DidYouMean);

    /// <summary>{0} = subcommand, {1} = group (with prefix), {2} = available subcommands.</summary>
    public const string UnknownSubcommand = nameof(UnknownSubcommand);

    /// <summary>{0} = group (with prefix), {1} = available subcommands, {2} = prefix, {3} = group name.</summary>
    public const string SubcommandRequired = nameof(SubcommandRequired);

    /// <summary>Line after a binding error. {0} = usage line.</summary>
    public const string Usage = nameof(Usage);

    /// <summary>{0} = option name.</summary>
    public const string MissingOption = nameof(MissingOption);

    /// <summary>{0} = option name.</summary>
    public const string OptionNeedsValue = nameof(OptionNeedsValue);

    /// <summary>{0} = parameter name.</summary>
    public const string MissingArgument = nameof(MissingArgument);

    /// <summary>{0} = first unexpected argument.</summary>
    public const string TooManyArguments = nameof(TooManyArguments);

    /// <summary>{0} = option name.</summary>
    public const string UnknownOption = nameof(UnknownOption);

    /// <summary>{0} = parameter name.</summary>
    public const string UnresolvedMention = nameof(UnresolvedMention);

    /// <summary>{0} = input, {1} = type name, {2} = parameter name.</summary>
    public const string InvalidArgument = nameof(InvalidArgument);

    /// <summary><c>[RequireAdmin]</c> failed.</summary>
    public const string RequireAdmin = nameof(RequireAdmin);

    /// <summary><c>[RequireGroup]</c> (or <c>[RequireGroupAdmin]</c> outside a group) failed.</summary>
    public const string RequireGroup = nameof(RequireGroup);

    /// <summary><c>[RequireDirectMessage]</c> failed.</summary>
    public const string RequireDirectMessage = nameof(RequireDirectMessage);

    /// <summary><c>[RequireGroupAdmin]</c> failed.</summary>
    public const string RequireGroupAdmin = nameof(RequireGroupAdmin);

    /// <summary><c>[RequireRole]</c> with one role failed. {0} = role.</summary>
    public const string RequireRole = nameof(RequireRole);

    /// <summary><c>[RequireRole]</c> with several roles failed. {0} = roles.</summary>
    public const string RequireAnyRole = nameof(RequireAnyRole);

    /// <summary><c>[Cooldown]</c> is active. {0} = seconds left.</summary>
    public const string Cooldown = nameof(Cooldown);

    /// <summary>Help list header.</summary>
    public const string HelpHeader = nameof(HelpHeader);

    /// <summary>Paged help list header. {0} = page, {1} = pages.</summary>
    public const string HelpHeaderPaged = nameof(HelpHeaderPaged);

    /// <summary>Heading of commands without group or category.</summary>
    public const string HelpGeneral = nameof(HelpGeneral);

    /// <summary>{0} = prefix, {1} = next page.</summary>
    public const string HelpMore = nameof(HelpMore);

    /// <summary>{0} = prefix.</summary>
    public const string HelpDetails = nameof(HelpDetails);

    /// <summary>{0} = prefix, {1} = group name.</summary>
    public const string HelpGroupDetails = nameof(HelpGroupDetails);

    /// <summary>{0} = aliases.</summary>
    public const string HelpAliases = nameof(HelpAliases);

    /// <summary>Heading of the examples of a command.</summary>
    public const string HelpExamples = nameof(HelpExamples);

    /// <summary><c>/help x</c> for an unknown x. {0} = x.</summary>
    public const string HelpUnknown = nameof(HelpUnknown);

    /// <summary>Page out of range with a single page.</summary>
    public const string HelpOnlyOnePage = nameof(HelpOnlyOnePage);

    /// <summary>Page out of range. {0} = pages.</summary>
    public const string HelpOnlyPages = nameof(HelpOnlyPages);

    /// <summary>A prompt answer could not be parsed. {0} = answer, {1} = type name, {2} = the question.</summary>
    public const string PromptInvalid = nameof(PromptInvalid);

    /// <summary>The prefix of type-name keys: <c>Type.{English display name}</c>.</summary>
    public const string TypePrefix = "Type.";

    /// <summary>The built-in English texts (keys without the options-backed ones).</summary>
    internal static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [DidYouMean] = " Did you mean {0}?",
        [UnknownSubcommand] = "Unknown subcommand '{0}' for {1}. Available: {2}.",
        [SubcommandRequired] = "{0} needs a subcommand: {1}. Send {2}help {3} for details.",
        [Usage] = "Usage: {0}",
        [MissingOption] = "Missing required option --{0}.",
        [OptionNeedsValue] = "Option --{0} requires a value.",
        [MissingArgument] = "Missing argument <{0}>.",
        [TooManyArguments] = "Too many arguments ('{0}' was not expected).",
        [UnknownOption] = "Unknown option --{0}.",
        [UnresolvedMention] = "Could not resolve the @mention for <{0}>.",
        [InvalidArgument] = "'{0}' is not a valid {1} for <{2}>.",
        [RequireAdmin] = "This command is restricted to administrators.",
        [RequireGroup] = "This command can only be used in groups.",
        [RequireDirectMessage] = "This command can only be used in direct messages.",
        [RequireGroupAdmin] = "Only group admins can use this command.",
        [RequireRole] = "This command requires the {0} role.",
        [RequireAnyRole] = "This command requires one of these roles: {0}.",
        [Cooldown] = "Please wait {0} s before using this command again.",
        [HelpHeader] = "Available commands:",
        [HelpHeaderPaged] = "Available commands (page {0}/{1}):",
        [HelpGeneral] = "General",
        [HelpMore] = "Send {0}help {1} for more.",
        [HelpDetails] = "Send {0}help <command> for details.",
        [HelpGroupDetails] = "Send {0}help {1} <command> for details.",
        [HelpAliases] = "Aliases: {0}",
        [HelpExamples] = "Examples:",
        [HelpUnknown] = "Unknown command '{0}'.",
        [HelpOnlyOnePage] = "There is only 1 page of commands.",
        [HelpOnlyPages] = "There are only {0} pages of commands.",
        [PromptInvalid] = "'{0}' is not a valid {1}. {2}",
    };
}

/// <summary>
/// The texts the framework sends, per culture. The default reads translations from <c>Signal:Localization:Texts</c>
/// and falls back from a specific culture to its neutral culture (<c>de-AT</c> → <c>de</c>) and then to English.
/// Replace it to load texts from elsewhere (resources, a database).
/// </summary>
public interface ISignalTexts
{
    /// <summary>Gets a text, formatted with <paramref name="args"/>.</summary>
    /// <param name="key">A <see cref="TextKey"/> constant, or <c>Type.{display name}</c>.</param>
    /// <param name="culture">The culture of the conversation.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The text; the English default (or the key itself) if there is no translation.</returns>
    string Get(string key, CultureInfo culture, params object?[] args);
}

/// <summary>Resolves the culture of a message's conversation.</summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// The conversation's culture (<see cref="ConversationSettings.Culture"/>), otherwise
    /// <c>Signal:Localization:DefaultCulture</c>. Unknown culture names fall back to the default.
    /// </summary>
    /// <param name="context">The message.</param>
    /// <returns>The culture to reply in.</returns>
    public static async ValueTask<CultureInfo> GetCultureAsync(this MessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = await context.GetConversationSettingsAsync();
        return Resolve(settings?.Culture, context.Services);
    }

    /// <summary>
    /// The culture for synchronous framework code (binder, preconditions), using the settings the command middleware
    /// already loaded for this message; the default culture if none were loaded.
    /// </summary>
    internal static CultureInfo CultureOf(MessageContext context) =>
        Resolve(context.Items.TryGetValue(typeof(ConversationSettings), out var settings) ? (settings as ConversationSettings)?.Culture : null, context.Services);

    /// <summary>Resolves a text in the culture of <paramref name="context"/>'s conversation (synchronously, see <see cref="CultureOf"/>).</summary>
    internal static string Text(this MessageContext context, string key, params object?[] args) =>
        context.Services.GetRequiredService<ISignalTexts>().Get(key, CultureOf(context), args);

    internal static CultureInfo Resolve(string? cultureName, IServiceProvider services)
    {
        var fallback = services.GetRequiredService<IOptionsMonitor<SignalOptions>>().CurrentValue.Localization.DefaultCulture;
        foreach (var name in new[] { cultureName, fallback })
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(name);
                }
                catch (CultureNotFoundException)
                {
                    // Try the next candidate.
                }
            }
        }

        return CultureInfo.InvariantCulture;
    }
}

/// <summary>Configuration-backed <see cref="ISignalTexts"/>.</summary>
internal sealed partial class SignalTexts(IOptionsMonitor<SignalOptions> options, ILogger<SignalTexts> logger) : ISignalTexts
{
    public string Get(string key, CultureInfo culture, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);
        var fallback = Default(key);
        var template = Translation(key, culture) ?? fallback;
        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(culture, template, args);
        }
        catch (FormatException ex)
        {
            // A broken translation (e.g. "{3}" with two arguments) must not break the reply.
            LogInvalidTranslation(ex, key, culture.Name);
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
    }

    /// <summary>The translation for the culture or one of its parents (de-AT → de), if configured.</summary>
    private string? Translation(string key, CultureInfo culture)
    {
        var texts = options.CurrentValue.Localization.Texts;
        for (var current = culture; !Equals(current, CultureInfo.InvariantCulture); current = current.Parent)
        {
            var entry = texts.FirstOrDefault(t => string.Equals(t.Key, current.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (entry?.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) is { Value: { } text })
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>The configured or built-in English text; type names default to themselves.</summary>
    private string Default(string key)
    {
        var commands = options.CurrentValue.Commands;
        return key switch
        {
            TextKey.UnknownCommand => commands.UnknownCommandMessage,
            TextKey.DisabledCommand => commands.DisabledCommandMessage,
            TextKey.CommandError => commands.ErrorMessage,
            _ when key.StartsWith(TextKey.TypePrefix, StringComparison.Ordinal) => key[TextKey.TypePrefix.Length..],
            _ => TextKey.Defaults.TryGetValue(key, out var text) ? text : key,
        };
    }

    [LoggerMessage(LogLevel.Warning, "The '{Culture}' translation of '{Key}' has invalid placeholders; using the default text")]
    private partial void LogInvalidTranslation(Exception ex, string key, string culture);
}
