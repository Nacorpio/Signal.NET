using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signal.Application.Roles;

namespace Signal.Hosting;

/// <summary>Registration helpers added after <see cref="ISignalBuilder"/> was published (extension methods keep it non-breaking).</summary>
public static class SignalBuilderExtensions
{
    /// <summary>
    /// Adds an <see cref="IRoleProvider"/>, e.g. one backed by a database, next to the built-in configuration and group
    /// admin providers. A role is granted if any provider grants it.
    /// </summary>
    /// <typeparam name="TProvider">The provider type.</typeparam>
    /// <param name="builder">The Signal builder.</param>
    /// <param name="lifetime">The provider's lifetime; scoped (per message) by default, so it may use a <c>DbContext</c>.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static ISignalBuilder AddRoleProvider<TProvider>(this ISignalBuilder builder, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TProvider : class, IRoleProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Describe(typeof(IRoleProvider), typeof(TProvider), lifetime));
        return builder;
    }
}
