using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signal.Application.Commands.Binding;
using Signal.Application.Commands.Parsing;
using Signal.Application.Configuration;
using Signal.Application.Conversations;
using Signal.Application.Localization;
using Signal.Application.Pipeline;

namespace Signal.Application.Commands;

/// <summary>The command ran to completion.</summary>
/// <param name="Parsed">The parsed invocation.</param>
/// <param name="Command">The executed command.</param>
public sealed record CommandSucceeded(ParsedCommand Parsed, CommandDescriptor Command);

/// <summary>No command matches the typed name or alias.</summary>
/// <param name="Parsed">The parsed invocation.</param>
public sealed record CommandNotFound(ParsedCommand Parsed);

/// <summary>A precondition rejected the invocation; the command did not run.</summary>
/// <param name="Parsed">The parsed invocation.</param>
/// <param name="Command">The matched command.</param>
/// <param name="Reason">A user-facing reason, or <see langword="null"/> to fail silently.</param>
public sealed record CommandPreconditionFailed(ParsedCommand Parsed, CommandDescriptor Command, string? Reason);

/// <summary>The arguments could not be bound (missing, invalid, too many, unknown option); the command did not run.</summary>
/// <param name="Parsed">The parsed invocation.</param>
/// <param name="Command">The matched command.</param>
/// <param name="Error">A user-facing error message.</param>
public sealed record CommandBindingFailed(ParsedCommand Parsed, CommandDescriptor Command, string Error);

/// <summary>The command threw an exception.</summary>
/// <param name="Parsed">The parsed invocation.</param>
/// <param name="Command">The failed command.</param>
/// <param name="Exception">The exception thrown by the command.</param>
public sealed record CommandFaulted(ParsedCommand Parsed, CommandDescriptor Command, Exception Exception);

/// <summary>
/// Outcome of a command invocation: exactly one of <see cref="CommandSucceeded"/>, <see cref="CommandNotFound"/>,
/// <see cref="CommandPreconditionFailed"/>, <see cref="CommandBindingFailed"/> or <see cref="CommandFaulted"/>.
/// Stored in <see cref="MessageContext.Items"/> under <c>typeof(CommandResult)</c>.
/// </summary>
/// <remarks>
/// Each case carries exactly the data that exists for it (e.g. only <see cref="CommandFaulted"/> has an exception,
/// <see cref="CommandNotFound"/> has no command). Switch over the result to handle every outcome; the compiler
/// warns when a case is missing:
/// <code>
/// var text = result switch
/// {
///     CommandSucceeded =&gt; null,
///     CommandNotFound notFound =&gt; $"Unknown command {notFound.Parsed.Name}",
///     CommandPreconditionFailed failed =&gt; failed.Reason,
///     CommandBindingFailed failed =&gt; failed.Error,
///     CommandFaulted faulted =&gt; "Something went wrong",
/// };
/// </code>
/// </remarks>
public union CommandResult(CommandSucceeded, CommandNotFound, CommandPreconditionFailed, CommandBindingFailed, CommandFaulted)
{
    /// <summary>The parsed invocation, available for every outcome.</summary>
    /// <exception cref="InvalidOperationException">The result is <see langword="default"/> (holds no value).</exception>
    public ParsedCommand Parsed => this switch
    {
        CommandSucceeded succeeded => succeeded.Parsed,
        CommandNotFound notFound => notFound.Parsed,
        CommandPreconditionFailed failed => failed.Parsed,
        CommandBindingFailed failed => failed.Parsed,
        CommandFaulted faulted => faulted.Parsed,
        null => throw new InvalidOperationException("The command result has no value."),
    };

    /// <summary>Whether the command ran to completion.</summary>
    public bool IsSuccess => this is CommandSucceeded;
}

/// <summary>Resolves, checks, binds and runs a parsed command.</summary>
public interface ICommandExecutor
{
    /// <summary>Executes <paramref name="parsed"/>: lookup → preconditions → argument binding → invocation.</summary>
    /// <param name="message">The pipeline context of the triggering message.</param>
    /// <param name="parsed">The parsed invocation.</param>
    /// <returns>The outcome. Exceptions of the command are captured as <see cref="CommandFaulted"/>.</returns>
    Task<CommandResult> ExecuteAsync(MessageContext message, ParsedCommand parsed);
}

/// <summary>Default <see cref="ICommandExecutor"/>. Only cancellation caused by host shutdown propagates.</summary>
internal sealed partial class CommandExecutor(ICommandRegistry registry, ICommandArgumentBinder binder, ILogger<CommandExecutor> logger) : ICommandExecutor
{
    public async Task<CommandResult> ExecuteAsync(MessageContext message, ParsedCommand parsed)
    {
        if (!registry.TryGetCommand(parsed.Name, out var command))
        {
            // A grouped command: "/group sub args" parses as name "group" with arguments "sub args".
            if (parsed.Tokens is not [{ IsQuoted: false } sub, ..]
                || !registry.TryGetCommand($"{parsed.Name} {sub.Value}", out command))
            {
                return new CommandNotFound(parsed);
            }

            parsed = new ParsedCommand(
                parsed.Prefix,
                $"{parsed.Name} {sub.Value}",
                parsed.RawArguments[(sub.Start + sub.Value.Length)..].TrimStart());
        }

        var context = new CommandContext(message, parsed, command);
        if (await message.GetConversationSettingsAsync() is { } settings && settings.IsDisabled(command))
        {
            var disabled = message.Text(TextKey.DisabledCommand);
            return new CommandPreconditionFailed(parsed, command, string.IsNullOrEmpty(disabled) ? null : disabled);
        }

        foreach (var precondition in command.Preconditions)
        {
            var check = await precondition.CheckAsync(context, message.CancellationToken);
            if (!check.IsSuccess)
            {
                return new CommandPreconditionFailed(parsed, command, check.Reason);
            }
        }

        object?[] values;
        switch (binder.Bind(context))
        {
            case ArgumentBindingError error:
                return new CommandBindingFailed(parsed, command, error.Message);
            case BoundArguments bound:
                values = bound.Values;
                break;
            default:
                throw new InvalidOperationException($"{binder.GetType()} returned an empty binding result.");
        }

        try
        {
            LogExecuting(command.FullName, message.Sender.ToString());
            await command.Executor(context, values);
            return new CommandSucceeded(parsed, command);
        }
        catch (OperationCanceledException) when (message.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(ex, command.FullName);
            return new CommandFaulted(parsed, command, ex);
        }
    }

    [LoggerMessage(LogLevel.Information, "Executing command '{Command}' for {Sender}")]
    private partial void LogExecuting(string command, string sender);

    [LoggerMessage(LogLevel.Error, "Command '{Command}' failed")]
    private partial void LogFailed(Exception ex, string command);
}

/// <summary>Decides how the user is informed about a command's outcome. Replace it to customize error replies (e.g. localisation).</summary>
public interface ICommandResultHandler
{
    /// <summary>Reacts to the outcome of a command.</summary>
    /// <param name="context">The pipeline context of the triggering message.</param>
    /// <param name="result">The outcome.</param>
    /// <returns>A task that completes when the user was informed.</returns>
    Task HandleAsync(MessageContext context, CommandResult result);
}

/// <summary>
/// Default result handler: replies with the unknown-command message (if enabled), binding errors plus the usage
/// line, precondition reasons, or the generic error message for faulted commands. Exception details are never sent.
/// For a command group typed without a known subcommand it lists the group's commands instead. With
/// <see cref="CommandOptions.SuggestSimilarCommands"/> it appends the closest command or subcommand name.
/// </summary>
internal sealed class DefaultCommandResultHandler(IOptionsMonitor<SignalOptions> options, ICommandRegistry registry, ISignalTexts texts) : ICommandResultHandler
{
    public async Task HandleAsync(MessageContext context, CommandResult result)
    {
        var commands = options.CurrentValue.Commands;
        if (result.Value is CommandSucceeded)
        {
            return;
        }

        var settings = await context.GetConversationSettingsAsync();
        var culture = await context.GetCultureAsync();
        var reply = result switch
        {
            CommandSucceeded => null,
            CommandNotFound notFound => !commands.RespondToUnknown ? null
                : (GroupHint(notFound.Parsed, settings, culture)
                    ?? texts.Get(TextKey.UnknownCommand, culture, notFound.Parsed.Name, notFound.Parsed.Prefix))
                    + (commands.SuggestSimilarCommands ? Suggestion(notFound.Parsed, settings, culture) : null),
            CommandBindingFailed failed => $"{failed.Error}\n{texts.Get(TextKey.Usage, culture, failed.Command.FormatUsage(failed.Parsed.Prefix))}",
            CommandPreconditionFailed failed => failed.Reason,
            CommandFaulted => texts.Get(TextKey.CommandError, culture),
            null => null,
        };

        if (!string.IsNullOrEmpty(reply))
        {
            await context.ReplyAsync(reply, commands.QuoteReplies, context.CancellationToken);
        }
    }

    /// <summary>For <c>/group</c> or <c>/group unknown</c>: names the group's visible commands; otherwise <see langword="null"/>.</summary>
    private string? GroupHint(ParsedCommand parsed, ConversationSettings? settings, CultureInfo culture)
    {
        var visible = registry.GetGroup(parsed.Name).Where(c => IsAvailable(c, settings)).Select(c => c.Name).ToList();
        if (visible.Count == 0)
        {
            return null;
        }

        var group = parsed.Prefix + parsed.Name;
        var available = string.Join(", ", visible);
        return parsed.Tokens is [var sub, ..]
            ? texts.Get(TextKey.UnknownSubcommand, culture, sub.Value, group, available)
            : texts.Get(TextKey.SubcommandRequired, culture, group, available, parsed.Prefix, parsed.Name);
    }

    /// <summary>
    /// <c> Did you mean /x?</c> for the closest visible name: a subcommand of the group for <c>/group typo</c>,
    /// otherwise a top-level command, alias or group name. Empty when nothing is close enough.
    /// </summary>
    private string? Suggestion(ParsedCommand parsed, ConversationSettings? settings, CultureInfo culture)
    {
        var group = registry.GetGroup(parsed.Name).Where(c => IsAvailable(c, settings)).ToList();
        if (group.Count > 0)
        {
            return parsed.Tokens is [var sub, ..]
                && CommandSuggestions.Closest(sub.Value, group.SelectMany(c => c.Aliases.Prepend(c.Name))) is { } closestSub
                    ? texts.Get(TextKey.DidYouMean, culture, $"{parsed.Prefix}{parsed.Name} {closestSub}")
                    : null;
        }

        var candidates = registry.Commands
            .Where(c => IsAvailable(c, settings))
            .SelectMany(c => c.Group is { } g ? g.Aliases.Prepend(g.Name) : c.Aliases.Prepend(c.Name));
        return CommandSuggestions.Closest(parsed.Name, candidates) is { } closest ? texts.Get(TextKey.DidYouMean, culture, parsed.Prefix + closest) : null;
    }

    /// <summary>Visible in help and suggestions: not hidden and not disabled in the conversation.</summary>
    private static bool IsAvailable(CommandDescriptor command, ConversationSettings? settings) =>
        !command.Hidden && settings?.IsDisabled(command) != true;
}

/// <summary>
/// Last built-in pipeline step: parses prefixed text messages, executes the matching command, marks the message as
/// handled and passes the outcome to <see cref="ICommandResultHandler"/>. Reactions are never treated as commands.
/// </summary>
/// <param name="parser">The command parser.</param>
/// <param name="executor">The command executor.</param>
/// <param name="results">The result handler.</param>
public sealed class CommandMiddleware(ICommandParser parser, ICommandExecutor executor, ICommandResultHandler results) : IMessageMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        if (!context.IsHandled
            && context.Envelope.Data is { Text: { } text, Reaction: null }
            && await TryParseAsync(context, text) is { } parsed)
        {
            var result = await executor.ExecuteAsync(context, parsed);
            context.Items[typeof(CommandResult)] = result;
            context.IsHandled = true;
            await results.HandleAsync(context, result);
        }

        await next(context);
    }

    /// <summary>Parses with the conversation's own prefixes if it has any (<see cref="ConversationSettings.Prefixes"/>).</summary>
    private async ValueTask<ParsedCommand?> TryParseAsync(MessageContext context, string text)
    {
        var prefixes = (await context.GetConversationSettingsAsync())?.Prefixes;
        return (prefixes is { Count: > 0 } ? parser.TryParse(text, prefixes, out var parsed) : parser.TryParse(text, out parsed)) ? parsed : null;
    }
}
