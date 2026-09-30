using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Signal.Application.Abstractions;
using Signal.Application.Commands;
using Signal.Application.Pipeline;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Reactions;

/// <summary>
/// Runs the method when someone reacts with one of the emojis, by default only to messages the bot's account sent
/// (polls, approvals). Emojis match regardless of skin tone and variation selector, so <c>👍</c> also matches
/// <c>👍🏽</c> and <c>❤</c> matches <c>❤️</c>.
/// </summary>
/// <example>
/// <code>
/// public sealed class ApprovalModule : ReactionModule
/// {
///     [OnReaction("👍")] public Task ApproveAsync() =&gt; ReplyAsync($"Approved by {Context.Sender}.");
/// }
/// </code>
/// </example>
/// <param name="emojis">The emojis that trigger the method.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnReactionAttribute(params string[] emojis) : Attribute
{
    /// <summary>The emojis that trigger the method.</summary>
    public IReadOnlyList<string> Emojis { get; } = emojis is { Length: > 0 }
        ? emojis
        : throw new ArgumentException("At least one emoji is required.", nameof(emojis));

    /// <summary>Also run when a matching reaction is removed (<see cref="Reaction.IsRemove"/>). Default <see langword="false"/>.</summary>
    public bool IncludeRemovals { get; set; }

    /// <summary>Run for reactions to anyone's messages, not only the bot account's own. Default <see langword="false"/>.</summary>
    public bool AnyMessage { get; set; }
}

/// <summary>Everything about a reaction that triggered an <see cref="OnReactionAttribute"/> method.</summary>
/// <param name="message">The message context of the reaction envelope.</param>
/// <param name="reaction">The reaction.</param>
public sealed class ReactionContext(MessageContext message, Reaction reaction)
{
    /// <summary>The pipeline context of the reaction envelope.</summary>
    public MessageContext Message { get; } = message;

    /// <summary>The reaction: emoji, target author and timestamp, and whether it was removed.</summary>
    public Reaction Reaction { get; } = reaction;

    /// <summary>Who reacted.</summary>
    public Sender Sender => Message.Sender;

    /// <summary>The receiving account.</summary>
    public PhoneNumber Account => Message.Account;

    /// <summary>The conversation of the reaction (group or direct chat).</summary>
    public Recipient Conversation => Message.Conversation;

    /// <summary>The message's scoped services.</summary>
    public IServiceProvider Services => Message.Services;

    /// <summary>Cancelled when the host shuts down.</summary>
    public CancellationToken CancellationToken => Message.CancellationToken;

    /// <summary>Sends a text into the conversation of the reaction.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(string text) => Message.ReplyAsync(text);
}

/// <summary>
/// Base class for reaction handlers: public methods with <see cref="OnReactionAttribute"/>. A new instance is created
/// per reaction from the message's DI scope, so constructor injection works. Methods may take
/// <see cref="ReactionContext"/>, <see cref="Reaction"/> and <see cref="CancellationToken"/> parameters and return
/// <see cref="Task"/>, <see cref="ValueTask"/> or <see langword="void"/>.
/// </summary>
public abstract class ReactionModule
{
    /// <summary>The reaction being handled. Set by the framework before the method is invoked.</summary>
    public ReactionContext Context { get; internal set; } = null!;

    /// <summary>Replies into the conversation of the reaction.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The send result.</returns>
    protected Task<SendResult> ReplyAsync(string text) => Context.ReplyAsync(text);
}

/// <summary>A discovered <see cref="OnReactionAttribute"/> method.</summary>
internal sealed record ReactionHandler(Type ModuleType, string Name, OnReactionAttribute Trigger, IReadOnlySet<string> Emojis, Func<ReactionContext, Task> Invoke);

/// <summary>
/// Last built-in pipeline step: runs the <see cref="OnReactionAttribute"/> methods matching a reaction. A failing
/// handler is logged and doesn't stop the others. Marks the message as handled if any handler ran.
/// </summary>
internal sealed partial class ReactionMiddleware(CommandCatalog catalog, ILogger<ReactionMiddleware> logger) : IMessageMiddleware
{
    private readonly Lazy<IReadOnlyList<ReactionHandler>> _handlers = new(() => [.. catalog.ReactionModuleTypes.SelectMany(Discover)]);

    public async Task InvokeAsync(MessageContext context, MessageDelegate next)
    {
        if (context.Envelope.Data is { Reaction: { } reaction })
        {
            var emoji = Normalize(reaction.Emoji);
            var ownMessage = PhoneNumber.TryParse(reaction.TargetAuthor, out var author) && author == context.Account;
            foreach (var handler in _handlers.Value)
            {
                if (!handler.Emojis.Contains(emoji)
                    || (reaction.IsRemove && !handler.Trigger.IncludeRemovals)
                    || (!ownMessage && !handler.Trigger.AnyMessage))
                {
                    continue;
                }

                try
                {
                    await handler.Invoke(new ReactionContext(context, reaction));
                    context.IsHandled = true;
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogHandlerFailed(ex, handler.ModuleType.Name, handler.Name);
                }
            }
        }

        await next(context);
    }

    /// <summary>
    /// Removes skin-tone modifiers (U+1F3FB–U+1F3FF) and variation selectors (U+FE0E, U+FE0F), so different renderings
    /// of the same emoji match.
    /// </summary>
    internal static string Normalize(string emoji)
    {
        var builder = new StringBuilder(emoji.Length);
        foreach (var rune in emoji.EnumerateRunes())
        {
            if (rune.Value is not (0xFE0E or 0xFE0F or (>= 0x1F3FB and <= 0x1F3FF)))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    /// <summary>Throws if a handler of <paramref name="moduleType"/> has an unsupported signature.</summary>
    internal static void Validate(Type moduleType) => _ = Discover(moduleType).ToList();

    /// <summary>Finds the <see cref="OnReactionAttribute"/> methods of a module and compiles their invocation.</summary>
    private static IEnumerable<ReactionHandler> Discover(Type moduleType)
    {
        var factory = ActivatorUtilities.CreateFactory(moduleType, Type.EmptyTypes);
        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<OnReactionAttribute>() is not { } trigger)
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.FirstOrDefault(p => p.ParameterType != typeof(ReactionContext) && p.ParameterType != typeof(Reaction) && p.ParameterType != typeof(CancellationToken)) is { } unsupported)
            {
                throw new InvalidOperationException(
                    $"Reaction handler {moduleType.Name}.{method.Name} has an unsupported parameter '{unsupported.Name}'; use ReactionContext, Reaction or CancellationToken.");
            }

            var invoker = CommandDescriptorFactory.CompileInvoker(method);
            async Task Invoke(ReactionContext context)
            {
                var module = (ReactionModule)factory(context.Services, null);
                module.Context = context;
                try
                {
                    await invoker(module, [.. parameters.Select(p =>
                        p.ParameterType == typeof(ReactionContext) ? context
                        : p.ParameterType == typeof(Reaction) ? context.Reaction
                        : (object)context.CancellationToken)]);
                }
                finally
                {
                    if (module is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else if (module is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            yield return new ReactionHandler(moduleType, method.Name, trigger, trigger.Emojis.Select(Normalize).ToHashSet(StringComparer.Ordinal), Invoke);
        }
    }

    [LoggerMessage(LogLevel.Error, "Reaction handler {Module}.{Method} failed")]
    private partial void LogHandlerFailed(Exception ex, string module, string method);
}
