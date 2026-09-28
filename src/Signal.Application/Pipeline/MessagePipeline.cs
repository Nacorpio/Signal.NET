using Microsoft.Extensions.DependencyInjection;

namespace Signal.Application.Pipeline;

/// <summary>A step of the pipeline, representing "the rest of the pipeline" when passed to a middleware.</summary>
/// <param name="context">The message being processed.</param>
/// <returns>A task that completes when the remaining pipeline has run.</returns>
public delegate Task MessageDelegate(MessageContext context);

/// <summary>
/// A step of the incoming message pipeline (ASP.NET Core style). Instances are resolved from the message's
/// DI scope, so constructor injection of scoped services is supported.
/// </summary>
/// <example>
/// <code>
/// public sealed class TypingMiddleware(ITypingIndicatorService typing) : IMessageMiddleware
/// {
///     public async Task InvokeAsync(MessageContext context, MessageDelegate next)
///     {
///         await typing.StartTypingAsync(context.Account, context.Conversation);
///         try { await next(context); }
///         finally { await typing.StopTypingAsync(context.Account, context.Conversation); }
///     }
/// }
/// </code>
/// </example>
public interface IMessageMiddleware
{
    /// <summary>Processes the message. Call <paramref name="next"/> to continue; skip it to stop processing.</summary>
    /// <param name="context">The message being processed.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>A task that completes when this step (and everything it invoked) has finished.</returns>
    Task InvokeAsync(MessageContext context, MessageDelegate next);
}

/// <summary>Runs a message through all registered middlewares.</summary>
public interface IMessagePipeline
{
    /// <summary>Executes the pipeline for one message.</summary>
    /// <param name="context">The message, with its own DI scope.</param>
    /// <returns>A task that completes when processing finished.</returns>
    Task ExecuteAsync(MessageContext context);
}

/// <summary>
/// Ordered list of middleware types. Built-in guards run first (exception handling, logging, access control,
/// rate limiting), then user middleware in registration order, then event dispatching and finally command handling.
/// </summary>
public sealed class MiddlewareRegistry
{
    private readonly List<Type> _leading = [];
    private readonly List<Type> _user = [];
    private readonly List<Type> _trailing = [];

    /// <summary>All middleware types in execution order.</summary>
    public IReadOnlyList<Type> Middlewares => [.. _leading, .. _user, .. _trailing];

    /// <summary>Adds a user middleware (after the built-in guards, before events and commands). Duplicates are ignored.</summary>
    /// <param name="middlewareType">A type implementing <see cref="IMessageMiddleware"/>; it must also be registered in DI.</param>
    /// <exception cref="ArgumentException">The type does not implement <see cref="IMessageMiddleware"/>.</exception>
    public void Add(Type middlewareType) => Add(_user, middlewareType);

    /// <summary>Adds a built-in middleware that runs before user middleware.</summary>
    internal void AddLeading(Type middlewareType) => Add(_leading, middlewareType);

    /// <summary>Adds a built-in middleware that runs after user middleware.</summary>
    internal void AddTrailing(Type middlewareType) => Add(_trailing, middlewareType);

    private static void Add(List<Type> list, Type type)
    {
        if (!typeof(IMessageMiddleware).IsAssignableFrom(type))
        {
            throw new ArgumentException($"{type} does not implement {nameof(IMessageMiddleware)}.", nameof(type));
        }

        if (!list.Contains(type))
        {
            list.Add(type);
        }
    }
}

/// <summary>
/// Default <see cref="IMessagePipeline"/>. The delegate chain is composed once (singleton); each step resolves
/// its middleware instance from the message scope when invoked.
/// </summary>
internal sealed class MessagePipeline : IMessagePipeline
{
    private readonly MessageDelegate _entry;

    public MessagePipeline(MiddlewareRegistry registry)
    {
        MessageDelegate next = static _ => Task.CompletedTask;
        foreach (var type in registry.Middlewares.Reverse())
        {
            var following = next;
            next = context => ((IMessageMiddleware)context.Services.GetRequiredService(type)).InvokeAsync(context, following);
        }

        _entry = next;
    }

    public Task ExecuteAsync(MessageContext context) => _entry(context);
}
