using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Commands;
using Signal.Application.Commands.Preconditions;
using Signal.Application.Configuration;
using Signal.Application.Localization;
using Signal.Application.Pipeline;

namespace Signal.Application.Roles;

/// <summary>Built-in role names. Role names are compared case-insensitively.</summary>
public static class Role
{
    /// <summary>Senders listed in <c>Signal:Commands:Admins</c> (and <c>Roles:admin</c>).</summary>
    public const string Admin = "admin";

    /// <summary>Admins of the Signal group the message was sent in (queried live); never granted in direct messages.</summary>
    public const string GroupAdmin = "group-admin";
}

/// <summary>
/// Decides whether a message's sender has a role. Several providers can be registered (e.g. configuration plus a
/// database); a role is granted if any of them grants it. Register with <c>AddRoleProvider&lt;T&gt;()</c>; providers are
/// resolved from the message scope, so they may use scoped services.
/// </summary>
public interface IRoleProvider
{
    /// <summary>Whether the sender of <paramref name="message"/> has <paramref name="role"/> in its conversation.</summary>
    /// <param name="message">The message (sender, conversation, services).</param>
    /// <param name="role">The role name.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> to grant the role; <see langword="false"/> if this provider doesn't grant it.</returns>
    ValueTask<bool> HasRoleAsync(MessageContext message, string role, CancellationToken cancellationToken);
}

/// <summary>Checks roles across all registered <see cref="IRoleProvider"/>s.</summary>
public interface IRoleService
{
    /// <summary>Whether the sender has at least one of <paramref name="roles"/>.</summary>
    /// <param name="message">The message.</param>
    /// <param name="roles">Acceptable roles.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> if any provider grants any of the roles.</returns>
    ValueTask<bool> HasAnyRoleAsync(MessageContext message, IEnumerable<string> roles, CancellationToken cancellationToken = default);
}

/// <summary>Asks each provider for each role until one grants it.</summary>
internal sealed class RoleService(IEnumerable<IRoleProvider> providers) : IRoleService
{
    public async ValueTask<bool> HasAnyRoleAsync(MessageContext message, IEnumerable<string> roles, CancellationToken cancellationToken = default)
    {
        foreach (var role in roles)
        {
            foreach (var provider in providers)
            {
                if (await provider.HasRoleAsync(message, role, cancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Grants roles from <c>Signal:Commands:Roles</c> (role → phone numbers or UUIDs). <see cref="Role.Admin"/> also
/// includes <c>Signal:Commands:Admins</c>. Changes apply without restart.
/// </summary>
internal sealed class ConfigurationRoleProvider(IOptionsMonitor<SignalOptions> options) : IRoleProvider
{
    public ValueTask<bool> HasRoleAsync(MessageContext message, string role, CancellationToken cancellationToken)
    {
        var commands = options.CurrentValue.Commands;
        var members = commands.Roles
            .Where(r => string.Equals(r.Key, role, StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Value);
        if (string.Equals(role, Role.Admin, StringComparison.OrdinalIgnoreCase))
        {
            members = members.Concat(commands.Admins);
        }

        return ValueTask.FromResult(members.Any(message.Sender.Matches));
    }
}

/// <summary>Grants <see cref="Role.GroupAdmin"/> to admins of the current Signal group, queried through <see cref="IGroupService"/>.</summary>
internal sealed class GroupAdminRoleProvider : IRoleProvider
{
    public async ValueTask<bool> HasRoleAsync(MessageContext message, string role, CancellationToken cancellationToken)
    {
        if (!string.Equals(role, Role.GroupAdmin, StringComparison.OrdinalIgnoreCase) || message.Envelope.Group is not { } groupId)
        {
            return false;
        }

        var group = await message.Services.GetRequiredService<IGroupService>().GetAsync(message.Account, groupId, cancellationToken);
        var sender = message.Sender;
        return group is not null
            && ((sender.Number is { } n && group.IsAdmin(n.Value)) || (sender.Uuid is { } u && group.IsAdmin(u.ToString("D"))));
    }
}

/// <summary>
/// The sender must have at least one of the given roles, as decided by the registered <see cref="IRoleProvider"/>s
/// (by default: <c>Signal:Commands:Roles</c>, <c>Signal:Commands:Admins</c> for <see cref="Role.Admin"/>, and Signal
/// group admins for <see cref="Role.GroupAdmin"/>).
/// </summary>
/// <example>
/// <code>
/// [Command("ban"), RequireRole("moderator", Role.GroupAdmin)]
/// public Task BanAsync(Recipient member) =&gt; …;
/// </code>
/// </example>
/// <param name="roles">Acceptable roles; one is enough.</param>
public sealed class RequireRoleAttribute(params string[] roles) : PreconditionAttribute
{
    /// <summary>Acceptable roles; one is enough.</summary>
    public IReadOnlyList<string> Roles { get; } = roles is { Length: > 0 }
        ? roles
        : throw new ArgumentException("At least one role is required.", nameof(roles));

    /// <inheritdoc />
    public override async ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken) =>
        await context.Services.GetRequiredService<IRoleService>().HasAnyRoleAsync(context.Message, Roles, cancellationToken)
            ? PreconditionResult.Success
            : Roles.Count == 1
                ? Fail(context, TextKey.RequireRole, Roles[0])
                : Fail(context, TextKey.RequireAnyRole, string.Join(", ", Roles));
}
