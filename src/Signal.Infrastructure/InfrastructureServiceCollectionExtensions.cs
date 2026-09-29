using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Configuration;
using Signal.Domain;
using Signal.Infrastructure.Http;
using Signal.Infrastructure.Receiving;
using Signal.Infrastructure.Services;

namespace Signal.Infrastructure;

/// <summary>DI registration of the infrastructure layer.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the signal-cli-rest-api adapters for all application ports, the typed HTTP client with its
    /// resilience pipeline, and the receivers. Usually called through <c>AddSignal()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Receivers are keyed services, keyed by <see cref="ExecutionMode"/>: <see cref="PollingMessageReceiver"/> for
    /// <c>normal</c>/<c>native</c> and <see cref="WebSocketMessageReceiver"/> for <c>json-rpc</c>/<c>json-rpc-native</c>.
    /// Resolving the non-keyed <see cref="IMessageReceiver"/> returns the one for the configured mode.
    /// </para>
    /// <para>
    /// HTTP resilience (standard pipeline): per-attempt timeout = <see cref="HttpOptions.Timeout"/>, retries with
    /// exponential backoff for GET/PUT/DELETE only (POST is never retried to avoid duplicate messages), and a circuit
    /// breaker. All services use <c>TryAdd</c>; register replacements beforehand to override them.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSignalInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient<SignalApiClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<SignalOptions>>().Value;
                client.BaseAddress = WithTrailingSlash(options.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan; // enforced by the resilience pipeline
            })
            .AddStandardResilienceHandler()
            .Configure((resilience, sp) =>
            {
                var http = sp.GetRequiredService<IOptions<SignalOptions>>().Value.Http;
                resilience.AttemptTimeout.Timeout = http.Timeout;
                resilience.TotalRequestTimeout.Timeout = http.Timeout * (http.RetryCount + 1) + TimeSpan.FromSeconds(5);

                // The circuit breaker requires a sampling duration of at least twice the attempt timeout.
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(http.Timeout.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));

                // Sending twice is worse than failing once: only idempotent methods are retried.
                resilience.Retry.DisableForUnsafeHttpMethods();
                if (http.RetryCount == 0)
                {
                    resilience.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                }
                else
                {
                    resilience.Retry.MaxRetryAttempts = http.RetryCount;
                }
            });

        services.TryAddTransient<IMessageSender, RestMessageSender>();
        services.TryAddTransient<IReactionService, RestReactionService>();
        services.TryAddTransient<IReceiptService, RestReceiptService>();
        services.TryAddTransient<ITypingIndicatorService, RestTypingIndicatorService>();
        services.TryAddTransient<IGroupService, RestGroupService>();
        services.TryAddTransient<IAccountService, RestAccountService>();
        services.TryAddTransient<IRegistrationService, RestRegistrationService>();
        services.TryAddTransient<IDeviceService, RestDeviceService>();
        services.TryAddTransient<IContactService, RestContactService>();
        services.TryAddTransient<IAttachmentService, RestAttachmentService>();
        services.TryAddTransient<IProfileService, RestProfileService>();
        services.TryAddTransient<IIdentityService, RestIdentityService>();
        services.TryAddTransient<ISystemService, RestSystemService>();

        services.TryAddSingleton<IWebSocketConnector, ClientWebSocketConnector>();
        services.TryAddKeyedTransient<IMessageReceiver, PollingMessageReceiver>(ExecutionMode.Normal);
        services.TryAddKeyedTransient<IMessageReceiver, PollingMessageReceiver>(ExecutionMode.Native);
        services.TryAddKeyedTransient<IMessageReceiver, WebSocketMessageReceiver>(ExecutionMode.JsonRpc);
        services.TryAddKeyedTransient<IMessageReceiver, WebSocketMessageReceiver>(ExecutionMode.JsonRpcNative);
        services.TryAddSingleton<IMessageReceiverFactory, KeyedMessageReceiverFactory>();

        // The receiver matching the configured mode.
        services.TryAddTransient(sp => sp.GetRequiredService<IMessageReceiverFactory>()
            .Create(sp.GetRequiredService<IOptions<SignalOptions>>().Value.Mode));

        return services;
    }

    /// <summary>Relative paths such as <c>v2/send</c> only resolve below the base path if it ends with a slash.</summary>
    private static Uri WithTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    /// <summary>Resolves receivers registered as keyed services by <see cref="ExecutionMode"/>.</summary>
    private sealed class KeyedMessageReceiverFactory(IServiceProvider services) : IMessageReceiverFactory
    {
        public IMessageReceiver Create(ExecutionMode mode) =>
            services.GetKeyedService<IMessageReceiver>(mode)
            ?? throw new InvalidOperationException($"No {nameof(IMessageReceiver)} registered for execution mode {mode}.");
    }
}
