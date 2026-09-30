using System.Reflection;
using Signal.Application.Abstractions;
using Signal.Application.Background;

namespace Signal.Application.Commands;

/// <summary>
/// A class-based command. Instances are resolved from the message's DI scope, so constructor injection works.
/// Arguments are not bound automatically; read <see cref="CommandContext.Arguments"/> and <see cref="CommandContext.Flags"/>.
/// </summary>
/// <remarks>Derive from <see cref="CommandBase"/> to take metadata from a <see cref="CommandAttribute"/>.</remarks>
public interface ICommand
{
    /// <summary>The command name (typed after the prefix).</summary>
    string Name { get; }

    /// <summary>Alternative names.</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>One-line description for the help listing.</summary>
    string? Description { get; }

    /// <summary>Usage text without prefix, e.g. <c>ban &lt;number&gt; [reason...]</c>.</summary>
    string? Usage { get; }

    /// <summary>Whether the command is hidden from the help listing.</summary>
    bool Hidden => false;

    /// <summary>Executes the command.</summary>
    /// <param name="context">The command context (message, arguments, services, reply helpers).</param>
    /// <param name="cancellationToken">Cancelled when the host shuts down.</param>
    /// <returns>A task that completes when the command has finished.</returns>
    Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Base class for class-based commands. Metadata defaults to the <see cref="CommandAttribute"/> on the class and
/// every member can be overridden.
/// </summary>
/// <example>
/// <code>
/// [Command("ping", Description = "Checks that the bot is alive")]
/// public sealed class PingCommand : CommandBase
/// {
///     public override Task ExecuteAsync(CommandContext context, CancellationToken ct) =>
///         context.ReplyAsync("pong", cancellationToken: ct);
/// }
/// </code>
/// </example>
public abstract class CommandBase : ICommand
{
    private CommandAttribute? Attribute => GetType().GetCustomAttribute<CommandAttribute>();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Neither overridden nor declared through <see cref="CommandAttribute"/>.</exception>
    public virtual string Name => Attribute?.Name
        ?? throw new InvalidOperationException($"{GetType()} must override {nameof(Name)} or be annotated with [{nameof(CommandAttribute)}].");

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Aliases => Attribute?.Aliases ?? [];

    /// <inheritdoc />
    public virtual string? Description => Attribute?.Description;

    /// <inheritdoc />
    public virtual string? Usage => Attribute?.Usage;

    /// <inheritdoc />
    public virtual bool Hidden => Attribute?.Hidden ?? false;

    /// <inheritdoc />
    public abstract Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Groups several commands as <c>[Command]</c> methods. Parameters are bound from the message text by type;
/// <see cref="CommandContext"/> and <see cref="CancellationToken"/> parameters are injected. A new module instance
/// is created (with constructor injection from the message scope) for every execution and disposed afterwards.
/// </summary>
/// <remarks>
/// Methods may return <see langword="void"/>, <see cref="Task"/>, <see cref="Task{TResult}"/>,
/// <see cref="ValueTask"/> or <see cref="ValueTask{TResult}"/>. Preconditions on the module class apply to all its commands.
/// </remarks>
public abstract class CommandModule
{
    /// <summary>The context of the executing command. Set by the framework before the method is invoked.</summary>
    public CommandContext Context { get; internal set; } = null!;

    /// <summary>Replies into the conversation of the triggering message.</summary>
    /// <param name="text">The reply text.</param>
    /// <param name="quote">Quote the triggering message; defaults to <c>Signal:Commands:QuoteReplies</c>.</param>
    /// <returns>The send result.</returns>
    protected Task<SendResult> ReplyAsync(string text, bool? quote = null) =>
        Context.ReplyAsync(text, quote, Context.CancellationToken);

    /// <summary>Reacts to the triggering message with an emoji.</summary>
    /// <param name="emoji">The reaction emoji.</param>
    /// <returns>A task that completes when the reaction was sent.</returns>
    protected Task ReactAsync(string emoji) => Context.ReactAsync(emoji, Context.CancellationToken);

    /// <summary>
    /// Runs slow work after the command returns, so later messages in the conversation aren't held up. The work gets
    /// its own DI scope; resolve services from <see cref="BackgroundWork.Services"/> rather than capturing
    /// the module's.
    /// </summary>
    /// <param name="work">The work; reply with <see cref="BackgroundWork.ReplyAsync(string)"/>.</param>
    /// <returns>A task that completes when the work was queued (not when it ran).</returns>
    /// <example>
    /// <code>
    /// [Command("report")]
    /// public async Task ReportAsync()
    /// {
    ///     await ReplyAsync("Working on it…");
    ///     await RunInBackgroundAsync(async work =&gt; await work.ReplyAsync(await BuildReportAsync(work.CancellationToken)));
    /// }
    /// </code>
    /// </example>
    protected ValueTask RunInBackgroundAsync(Func<BackgroundWork, Task> work) =>
        Context.Message.QueueBackgroundWorkAsync(work);
}
