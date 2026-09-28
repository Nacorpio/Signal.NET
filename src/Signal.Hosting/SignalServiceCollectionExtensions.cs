using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Signal.Application;
using Signal.Application.Configuration;
using Signal.Infrastructure;

namespace Signal.Hosting;

/// <summary>Entry points for adding Signal.NET to a .NET Generic Host.</summary>
public static class SignalServiceCollectionExtensions
{
    /// <summary>
    /// Adds Signal.NET, binding <see cref="SignalOptions"/> from the <c>Signal</c> configuration section.
    /// Registers the application and infrastructure layers and the background receiver.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>A builder to register commands, handlers and extensions.</returns>
    public static ISignalBuilder AddSignal(this IHostApplicationBuilder builder) =>
        builder.Services.AddSignal(builder.Configuration.GetSection(SignalOptions.SectionName));

    /// <summary>Adds Signal.NET, binding <see cref="SignalOptions"/> from <paramref name="configuration"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing the Signal settings.</param>
    /// <returns>A builder to register commands, handlers and extensions.</returns>
    public static ISignalBuilder AddSignal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var builder = services.AddSignalCore();
        services.AddOptions<SignalOptions>().Bind(configuration);
        return builder;
    }

    /// <summary>Adds Signal.NET, configuring <see cref="SignalOptions"/> in code.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options.</param>
    /// <returns>A builder to register commands, handlers and extensions.</returns>
    public static ISignalBuilder AddSignal(this IServiceCollection services, Action<SignalOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = services.AddSignalCore();
        services.Configure(configure);
        return builder;
    }

    /// <summary>Adds a health check that calls <c>GET /v1/health</c> on the container.</summary>
    /// <param name="builder">The health checks builder from <c>AddHealthChecks()</c>.</param>
    /// <param name="name">The check name.</param>
    /// <param name="failureStatus">Status reported when unhealthy; defaults to <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="tags">Tags for filtering; defaults to <c>signal</c>.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddSignalApi(this IHealthChecksBuilder builder, string name = "signal-api", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null) =>
        builder.AddCheck<SignalApiHealthCheck>(name, failureStatus, tags ?? ["signal"]);

    /// <summary>Registrations shared by all <c>AddSignal</c> overloads; options are validated on host start.</summary>
    private static SignalBuilder AddSignalCore(this IServiceCollection services)
    {
        services.AddOptions<SignalOptions>().ValidateOnStart();
        services.AddSignalApplication();
        services.AddSignalInfrastructure();
        services.AddHostedService<SignalHostedService>();
        return new SignalBuilder(services);
    }
}
