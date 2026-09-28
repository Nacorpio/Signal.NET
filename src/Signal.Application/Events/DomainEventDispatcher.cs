using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Signal.Domain.Events;

namespace Signal.Application.Events;

/// <summary>
/// Handles a domain event. Any number of handlers may exist per event type; they are resolved from the
/// message's DI scope and run sequentially in registration order.
/// </summary>
/// <typeparam name="TEvent">The handled event type, e.g. <see cref="MessageReceived"/>.</typeparam>
/// <example>
/// <code>
/// public sealed class Audit(ILogger&lt;Audit&gt; log) : IEventHandler&lt;GroupUpdated&gt;
/// {
///     public Task HandleAsync(GroupUpdated e, CancellationToken ct)
///     {
///         log.LogInformation("Group {Group} changed", e.Group);
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the event.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Cancelled when the host shuts down.</param>
    /// <returns>A task that completes when the event was handled.</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Delivers domain events to their handlers.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Invokes all <see cref="IEventHandler{TEvent}"/>s registered for the event's runtime type.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="cancellationToken">Passed to the handlers.</param>
    /// <returns>A task that completes when all handlers have run.</returns>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default dispatcher. Bridges from the runtime event type to the generic handler interface with one
/// compiled delegate per event type (cached), so dispatching does not use reflection per event.
/// </summary>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private delegate Task Invoker(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<Type, Invoker> Invokers = new();

    private static readonly MethodInfo InvokeHandlersMethod =
        typeof(DomainEventDispatcher).GetMethod(nameof(InvokeHandlersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var invoker = Invokers.GetOrAdd(domainEvent.GetType(),
            static type => InvokeHandlersMethod.MakeGenericMethod(type).CreateDelegate<Invoker>());
        return invoker(services, domainEvent, cancellationToken);
    }

    private static async Task InvokeHandlersAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }
}
