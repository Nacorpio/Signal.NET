using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Configuration;

namespace Signal.Application.Commands;

/// <summary>
/// Registration-time list of command sources, populated by the DI builder (<c>AddCommands</c>, <c>AddCommand</c>,
/// <c>AddCommandModule</c>, <c>MapCommand</c>) and consumed once by <see cref="ICommandRegistry"/>.
/// Registered in DI as a singleton instance.
/// </summary>
public sealed class CommandCatalog
{
    private readonly List<Type> _commandTypes = [];
    private readonly List<Type> _moduleTypes = [];
    private readonly List<CommandDescriptor> _descriptors = [];

    /// <summary>Registered <see cref="ICommand"/> implementations (must also be registered in DI).</summary>
    public IReadOnlyList<Type> CommandTypes => _commandTypes;

    /// <summary>Registered <see cref="CommandModule"/> types.</summary>
    public IReadOnlyList<Type> ModuleTypes => _moduleTypes;

    /// <summary>Directly registered descriptors (e.g. delegate commands).</summary>
    public IReadOnlyList<CommandDescriptor> Descriptors => _descriptors;

    /// <summary>Adds a class-based command type. Duplicates are ignored.</summary>
    /// <param name="commandType">A non-abstract <see cref="ICommand"/> implementation.</param>
    /// <exception cref="ArgumentException">The type is not a non-abstract <see cref="ICommand"/>.</exception>
    public void AddCommand(Type commandType)
    {
        if (!typeof(ICommand).IsAssignableFrom(commandType) || commandType.IsAbstract)
        {
            throw new ArgumentException($"{commandType} must be a non-abstract {nameof(ICommand)}.", nameof(commandType));
        }

        if (!_commandTypes.Contains(commandType))
        {
            _commandTypes.Add(commandType);
        }
    }

    /// <summary>Adds a command module type. Duplicates are ignored.</summary>
    /// <param name="moduleType">A non-abstract <see cref="CommandModule"/>.</param>
    /// <exception cref="ArgumentException">The type is not a non-abstract <see cref="CommandModule"/>.</exception>
    public void AddModule(Type moduleType)
    {
        if (!typeof(CommandModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract {nameof(CommandModule)}.", nameof(moduleType));
        }

        if (!_moduleTypes.Contains(moduleType))
        {
            _moduleTypes.Add(moduleType);
        }
    }

    /// <summary>Adds a ready-made descriptor.</summary>
    /// <param name="descriptor">The descriptor.</param>
    public void Add(CommandDescriptor descriptor) => _descriptors.Add(descriptor);
}

/// <summary>Looks up commands by name or alias.</summary>
public interface ICommandRegistry
{
    /// <summary>All distinct commands, ordered by name.</summary>
    IReadOnlyList<CommandDescriptor> Commands { get; }

    /// <summary>Finds a command by name or alias (case sensitivity per <see cref="CommandOptions.CaseSensitive"/>).</summary>
    /// <param name="nameOrAlias">The typed command name.</param>
    /// <param name="command">The command when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if a command matches.</returns>
    bool TryGetCommand(string nameOrAlias, [NotNullWhen(true)] out CommandDescriptor? command);
}

/// <summary>
/// Default registry. Builds the command table lazily on first use (after all registrations), validates that
/// names and aliases are unique, and throws <see cref="InvalidOperationException"/> on conflicts.
/// </summary>
internal sealed class CommandRegistry(CommandCatalog catalog, IServiceScopeFactory scopes, IOptions<SignalOptions> options) : ICommandRegistry
{
    private readonly Lazy<(IReadOnlyList<CommandDescriptor> All, Dictionary<string, CommandDescriptor> Lookup)> _state =
        new(() => Build(catalog, scopes, options.Value.Commands));

    public IReadOnlyList<CommandDescriptor> Commands => _state.Value.All;

    public bool TryGetCommand(string nameOrAlias, [NotNullWhen(true)] out CommandDescriptor? command) =>
        _state.Value.Lookup.TryGetValue(nameOrAlias, out command);

    private static (IReadOnlyList<CommandDescriptor>, Dictionary<string, CommandDescriptor>) Build(
        CommandCatalog catalog, IServiceScopeFactory scopes, CommandOptions options)
    {
        var descriptors = new List<CommandDescriptor>(catalog.Descriptors);

        // Class-based commands expose their metadata through instance properties, so resolve each once.
        using (var scope = scopes.CreateScope())
        {
            foreach (var type in catalog.CommandTypes)
            {
                var instance = (ICommand)scope.ServiceProvider.GetRequiredService(type);
                descriptors.Add(CommandDescriptorFactory.FromCommand(type, instance));
            }
        }

        foreach (var moduleType in catalog.ModuleTypes)
        {
            if (moduleType == typeof(HelpModule) && !options.EnableHelp)
            {
                continue;
            }

            descriptors.AddRange(CommandDescriptorFactory.FromModule(moduleType));
        }

        var comparer = options.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var lookup = new Dictionary<string, CommandDescriptor>(comparer);
        foreach (var descriptor in descriptors)
        {
            foreach (var key in descriptor.Aliases.Prepend(descriptor.Name))
            {
                if (!lookup.TryAdd(key, descriptor))
                {
                    throw new InvalidOperationException(
                        $"Command name or alias '{key}' is used by both {lookup[key].DeclaringType.Name}.{lookup[key].Name} and {descriptor.DeclaringType.Name}.{descriptor.Name}.");
                }
            }
        }

        return ([.. descriptors.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)], lookup);
    }
}
