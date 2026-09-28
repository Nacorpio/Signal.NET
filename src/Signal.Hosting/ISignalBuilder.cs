using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signal.Application;
using Signal.Application.Abstractions;
using Signal.Application.Commands;
using Signal.Application.Commands.Binding;
using Signal.Application.Configuration;
using Signal.Application.Events;
using Signal.Application.Pipeline;
using Signal.Domain;
using Signal.Domain.Events;

namespace Signal.Hosting;

/// <summary>
/// Fluent API returned by <c>AddSignal()</c> to extend Signal.NET: commands, middleware, event handlers,
/// argument converters and receivers. Every method returns the builder for chaining.
/// </summary>
/// <example>
/// <code>
/// builder.AddSignal()
///     .AddCommands(typeof(Program).Assembly)
///     .AddEventHandlers(typeof(Program).Assembly)
///     .AddMiddleware&lt;AuditMiddleware&gt;()
///     .MapCommand("about", ctx =&gt; ctx.ReplyAsync("Signal.NET bot"));
/// </code>
/// </example>
public interface ISignalBuilder
{
    /// <summary>The underlying service collection, for registrations the builder does not cover.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers all non-abstract <see cref="ICommand"/> implementations (as scoped services) and
    /// <see cref="CommandModule"/>s of an assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>This builder.</returns>
    ISignalBuilder AddCommands(Assembly assembly);

    /// <summary>Registers one class-based command (as a scoped service).</summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <returns>This builder.</returns>
    ISignalBuilder AddCommand<TCommand>()
        where TCommand : class, ICommand;

    /// <summary>Registers one command module.</summary>
    /// <typeparam name="TModule">The module type.</typeparam>
    /// <returns>This builder.</returns>
    ISignalBuilder AddCommandModule<TModule>()
        where TModule : CommandModule;

    /// <summary>Registers a lambda command, e.g. <c>MapCommand("ping", ctx =&gt; ctx.ReplyAsync("pong"))</c>.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="handler">The command body; read arguments through <see cref="CommandContext.Arguments"/>.</param>
    /// <param name="description">One-line description for help.</param>
    /// <param name="aliases">Alternative names.</param>
    /// <returns>This builder.</returns>
    ISignalBuilder MapCommand(string name, Func<CommandContext, Task> handler, string? description = null, params string[] aliases);

    /// <summary>
    /// Adds a middleware after the built-in guards and before event dispatching and command handling.
    /// Middlewares run in registration order and are resolved per message (scoped).
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type.</typeparam>
    /// <returns>This builder.</returns>
    ISignalBuilder AddMiddleware<TMiddleware>()
        where TMiddleware : class, IMessageMiddleware;

    /// <summary>Registers an event handler (scoped).</summary>
    /// <typeparam name="TEvent">The handled event type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <returns>This builder.</returns>
    ISignalBuilder AddEventHandler<TEvent, THandler>()
        where TEvent : IDomainEvent
        where THandler : class, IEventHandler<TEvent>;

    /// <summary>Registers all <see cref="IEventHandler{TEvent}"/> implementations of an assembly (a class may handle several events).</summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>This builder.</returns>
    ISignalBuilder AddEventHandlers(Assembly assembly);

    /// <summary>Registers an argument converter (singleton); it overrides built-in conversion of its target type.</summary>
    /// <typeparam name="TConverter">The converter type.</typeparam>
    /// <returns>This builder.</returns>
    ISignalBuilder AddArgumentConverter<TConverter>()
        where TConverter : class, IArgumentConverter;

    /// <summary>Replaces the receiver used for the given modes (all modes when none are given).</summary>
    /// <typeparam name="TReceiver">The receiver type (registered as transient keyed service).</typeparam>
    /// <param name="modes">The modes to replace the receiver for.</param>
    /// <returns>This builder.</returns>
    ISignalBuilder UseReceiver<TReceiver>(params ExecutionMode[] modes)
        where TReceiver : class, IMessageReceiver;

    /// <summary>Configures <see cref="SignalOptions"/> in code (applied after configuration binding).</summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder.</returns>
    ISignalBuilder Configure(Action<SignalOptions> configure);
}

/// <summary>Default <see cref="ISignalBuilder"/>; writes into the shared <see cref="CommandCatalog"/> and <see cref="MiddlewareRegistry"/> instances.</summary>
internal sealed class SignalBuilder(IServiceCollection services) : ISignalBuilder
{
    private CommandCatalog Catalog => Services.GetOrAddSingletonInstance<CommandCatalog>();

    public IServiceCollection Services { get; } = services;

    public ISignalBuilder AddCommands(Assembly assembly)
    {
        foreach (var type in ConcreteTypes(assembly))
        {
            if (typeof(ICommand).IsAssignableFrom(type))
            {
                AddCommandType(type);
            }
            else if (typeof(CommandModule).IsAssignableFrom(type) && type != typeof(HelpModule))
            {
                Catalog.AddModule(type);
            }
        }

        return this;
    }

    public ISignalBuilder AddCommand<TCommand>()
        where TCommand : class, ICommand
    {
        AddCommandType(typeof(TCommand));
        return this;
    }

    public ISignalBuilder AddCommandModule<TModule>()
        where TModule : CommandModule
    {
        Catalog.AddModule(typeof(TModule));
        return this;
    }

    public ISignalBuilder MapCommand(string name, Func<CommandContext, Task> handler, string? description = null, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Catalog.Add(new CommandDescriptor(name, (context, _) => handler(context), typeof(SignalBuilder), aliases, description));
        return this;
    }

    public ISignalBuilder AddMiddleware<TMiddleware>()
        where TMiddleware : class, IMessageMiddleware
    {
        Services.GetOrAddSingletonInstance<MiddlewareRegistry>().Add(typeof(TMiddleware));
        Services.TryAddScoped<TMiddleware>();
        return this;
    }

    public ISignalBuilder AddEventHandler<TEvent, THandler>()
        where TEvent : IDomainEvent
        where THandler : class, IEventHandler<TEvent>
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<TEvent>, THandler>());
        return this;
    }

    public ISignalBuilder AddEventHandlers(Assembly assembly)
    {
        foreach (var type in ConcreteTypes(assembly))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)))
            {
                Services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
            }
        }

        return this;
    }

    public ISignalBuilder AddArgumentConverter<TConverter>()
        where TConverter : class, IArgumentConverter
    {
        Services.AddSingleton<IArgumentConverter, TConverter>();
        return this;
    }

    public ISignalBuilder UseReceiver<TReceiver>(params ExecutionMode[] modes)
        where TReceiver : class, IMessageReceiver
    {
        foreach (var mode in modes.Length == 0 ? Enum.GetValues<ExecutionMode>() : modes)
        {
            Services.RemoveAllKeyed<IMessageReceiver>(mode);
            Services.AddKeyedTransient<IMessageReceiver, TReceiver>(mode);
        }

        return this;
    }

    public ISignalBuilder Configure(Action<SignalOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }

    /// <summary>Adds the type to the catalog and registers it in DI so it can be resolved per message.</summary>
    private void AddCommandType(Type type)
    {
        Catalog.AddCommand(type);
        Services.TryAddScoped(type);
    }

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });
}
