using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Background;
using Signal.Application.Commands;
using Signal.Application.Commands.Binding;
using Signal.Application.Commands.Parsing;
using Signal.Application.Commands.Preconditions;
using Signal.Application.Configuration;
using Signal.Application.Localization;
using Signal.Application.Conversations;
using Signal.Application.Events;
using Signal.Application.Pipeline;
using Signal.Application.Reactions;
using Signal.Application.Roles;
using Signal.Application.Scheduling;

namespace Signal.Application;

/// <summary>DI registration of the application layer.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application layer: options validation, the message pipeline with its built-in middleware,
    /// domain event dispatching and the command system (including the <c>help</c> command).
    /// Every service is registered with <c>TryAdd</c>, so implementations registered earlier take precedence.
    /// </summary>
    /// <remarks>
    /// Ports (<see cref="IMessageSender"/>, <see cref="IReactionService"/>, …) are not registered here; they come from
    /// the infrastructure layer or from your own implementations. Usually called through <c>AddSignal()</c>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSignalApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SignalOptions>, SignalOptionsValidator>());

        // Pipeline: guards → (user middleware) → events → commands → reaction handlers.
        var middlewares = services.GetOrAddSingletonInstance<MiddlewareRegistry>();
        middlewares.AddLeading(typeof(ExceptionHandlingMiddleware));
        middlewares.AddLeading(typeof(LoggingMiddleware));
        middlewares.AddLeading(typeof(AccessControlMiddleware));
        middlewares.AddLeading(typeof(RateLimitingMiddleware));
        middlewares.AddTrailing(typeof(DomainEventMiddleware));
        middlewares.AddTrailing(typeof(CommandMiddleware));
        middlewares.AddTrailing(typeof(ReactionMiddleware));
        foreach (var type in middlewares.Middlewares)
        {
            services.TryAddScoped(type);
        }

        services.TryAddSingleton<IMessagePipeline, MessagePipeline>();
        services.TryAddSingleton<ISenderRateLimiter, FixedWindowSenderRateLimiter>();

        // Background work: one in-memory queue, read by the processor that the host runs.
        services.TryAddSingleton<ChannelBackgroundWorkQueue>();
        services.TryAddSingleton<IBackgroundWorkQueue>(sp => sp.GetRequiredService<ChannelBackgroundWorkQueue>());
        services.TryAddSingleton<IBackgroundWorkProcessor, BackgroundWorkProcessor>();
        services.TryAddSingleton<IPromptRegistry, PromptRegistry>();
        services.TryAddSingleton<ISignalTexts, SignalTexts>();

        services.TryAddSingleton<IConversationSettingsStore, InMemoryConversationSettingsStore>();

        // Roles: configuration and Signal group admins; AddRoleProvider<T>() adds more.
        services.TryAddScoped<IRoleService, RoleService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoleProvider, ConfigurationRoleProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRoleProvider, GroupAdminRoleProvider>());

        // Scheduled messages: in-memory by default; register an IScheduledMessageStore first to persist them.
        services.TryAddSingleton<IScheduledMessageStore, InMemoryScheduledMessageStore>();
        services.TryAddSingleton<IMessageScheduler, MessageScheduler>();
        services.TryAddSingleton<IScheduledMessageDispatcher, ScheduledMessageDispatcher>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // Commands
        var catalog = services.GetOrAddSingletonInstance<CommandCatalog>();
        catalog.AddModule(typeof(HelpModule));
        services.TryAddSingleton<ICommandRegistry, CommandRegistry>();
        services.TryAddSingleton<ICommandParser, CommandParser>();
        services.TryAddSingleton<ICommandArgumentBinder, CommandArgumentBinder>();
        services.TryAddSingleton<IArgumentConverterProvider, ArgumentConverterProvider>();
        services.TryAddSingleton<ICooldownTracker, CooldownTracker>();
        services.TryAddScoped<ICommandExecutor, CommandExecutor>();
        services.TryAddScoped<ICommandResultHandler, DefaultCommandResultHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IArgumentConverter, StringArgumentConverter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IArgumentConverter, BooleanArgumentConverter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IArgumentConverter, RecipientArgumentConverter>());

        services.TryAddTransient<ISignalClient, SignalClient>();
        return services;
    }

    /// <summary>
    /// Returns the singleton instance registered for <typeparamref name="T"/>, registering a new one if needed.
    /// Used for registration-time state such as <see cref="CommandCatalog"/> and <see cref="MiddlewareRegistry"/>.
    /// </summary>
    /// <typeparam name="T">The service type, registered as an instance.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The shared instance.</returns>
    public static T GetOrAddSingletonInstance<T>(this IServiceCollection services)
        where T : class, new()
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(T) && !d.IsKeyedService)?.ImplementationInstance is T existing)
        {
            return existing;
        }

        var instance = new T();
        services.AddSingleton(instance);
        return instance;
    }
}
