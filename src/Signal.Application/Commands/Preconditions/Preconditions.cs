using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Configuration;
using Signal.Application.Localization;

namespace Signal.Application.Commands.Preconditions;

/// <summary>Outcome of a precondition check.</summary>
/// <param name="IsSuccess">Whether the command may run.</param>
/// <param name="Reason">A user-facing reason on failure; <see langword="null"/> fails silently.</param>
public readonly record struct PreconditionResult(bool IsSuccess, string? Reason)
{
    /// <summary>The check passed.</summary>
    public static PreconditionResult Success { get; } = new(true, null);

    /// <summary>The check failed.</summary>
    /// <param name="reason">A user-facing reason, or <see langword="null"/> to fail silently.</param>
    /// <returns>The result.</returns>
    public static PreconditionResult Fail(string? reason) => new(false, reason);
}

/// <summary>
/// A check executed before a command runs (and before its arguments are bound). Apply it to a command method,
/// an <see cref="ICommand"/> class, or a <see cref="CommandModule"/> class (then it applies to all its commands).
/// Derive from it to build custom preconditions; resolve services through <see cref="CommandContext.Services"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed class RequireWorkingHoursAttribute : PreconditionAttribute
/// {
///     public override ValueTask&lt;PreconditionResult&gt; CheckAsync(CommandContext context, CancellationToken ct) =&gt;
///         ValueTask.FromResult(DateTime.Now.Hour is &gt;= 8 and &lt; 18 ? PreconditionResult.Success : Fail("Office hours only."));
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class PreconditionAttribute : Attribute
{
    /// <summary>Overrides the default failure message. Set to an empty string to fail silently.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Execution order; lower values run first. Stateful checks such as cooldowns should run last.</summary>
    public virtual int Order => 0;

    /// <summary>Checks whether the command may run.</summary>
    /// <param name="context">The command context.</param>
    /// <param name="cancellationToken">Cancelled when the host shuts down.</param>
    /// <returns>The check result.</returns>
    public abstract ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken);

    /// <summary>Creates a failure honoring <see cref="ErrorMessage"/>.</summary>
    /// <param name="defaultMessage">The message used when <see cref="ErrorMessage"/> is not set.</param>
    /// <returns>The failed result.</returns>
    protected PreconditionResult Fail(string defaultMessage) =>
        PreconditionResult.Fail(ErrorMessage is null ? defaultMessage : ErrorMessage.Length == 0 ? null : ErrorMessage);

    /// <summary>
    /// Creates a failure with a localised default message (see <see cref="Localization.TextKey"/>), honoring
    /// <see cref="ErrorMessage"/>. Translations are looked up in the culture of the command's conversation.
    /// </summary>
    /// <param name="context">The command context.</param>
    /// <param name="textKey">The text key.</param>
    /// <param name="args">Format arguments of the text.</param>
    /// <returns>The failed result.</returns>
    protected PreconditionResult Fail(CommandContext context, string textKey, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ErrorMessage is null ? PreconditionResult.Fail(context.Message.Text(textKey, args)) : Fail(ErrorMessage);
    }
}

/// <summary>Only senders listed in <c>Signal:Commands:Admins</c> (phone numbers or UUIDs) may run the command.</summary>
public sealed class RequireAdminAttribute : PreconditionAttribute
{
    /// <inheritdoc />
    public override ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var admins = context.Services.GetRequiredService<IOptionsMonitor<SignalOptions>>().CurrentValue.Commands.Admins;
        return ValueTask.FromResult(admins.Any(context.Sender.Matches)
            ? PreconditionResult.Success
            : Fail(context, TextKey.RequireAdmin));
    }
}

/// <summary>The command may only be used in group conversations.</summary>
public sealed class RequireGroupAttribute : PreconditionAttribute
{
    /// <inheritdoc />
    public override ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.IsGroup ? PreconditionResult.Success : Fail(context, TextKey.RequireGroup));
}

/// <summary>The command may only be used in direct (1:1) conversations.</summary>
public sealed class RequireDirectMessageAttribute : PreconditionAttribute
{
    /// <inheritdoc />
    public override ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(!context.IsGroup ? PreconditionResult.Success : Fail(context, TextKey.RequireDirectMessage));
}

/// <summary>
/// The sender must be an admin of the Signal group the command was sent in (queried live through
/// <see cref="IGroupService"/>). Fails in direct messages.
/// </summary>
public sealed class RequireGroupAdminAttribute : PreconditionAttribute
{
    /// <inheritdoc />
    public override async ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Group is not { } groupId)
        {
            return Fail(context, TextKey.RequireGroup);
        }

        var group = await context.Services.GetRequiredService<IGroupService>().GetAsync(context.Account, groupId, cancellationToken);
        var isAdmin = group is not null
            && ((context.Sender.Number is { } n && group.IsAdmin(n.Value)) || (context.Sender.Uuid is { } u && group.IsAdmin(u.ToString("D"))));
        return isAdmin ? PreconditionResult.Success : Fail(context, TextKey.RequireGroupAdmin);
    }
}

/// <summary>Who shares a cooldown.</summary>
public enum CooldownScope
{
    /// <summary>Each sender has an independent cooldown.</summary>
    PerSender,

    /// <summary>Everyone in a conversation (group or direct chat) shares a cooldown.</summary>
    PerConversation,

    /// <summary>One cooldown for everybody.</summary>
    Global,
}

/// <summary>Limits how often a command can be used. Runs after all other preconditions.</summary>
/// <param name="seconds">The cooldown period in seconds.</param>
public sealed class CooldownAttribute(double seconds) : PreconditionAttribute
{
    /// <summary>The cooldown period.</summary>
    public TimeSpan Period { get; } = TimeSpan.FromSeconds(seconds);

    /// <summary>Who shares the cooldown. Default <see cref="CooldownScope.PerSender"/>.</summary>
    public CooldownScope Scope { get; set; } = CooldownScope.PerSender;

    /// <inheritdoc />
    public override int Order => 1000;

    /// <inheritdoc />
    public override ValueTask<PreconditionResult> CheckAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var scopeKey = Scope switch
        {
            CooldownScope.PerSender => context.Sender.Identifier,
            CooldownScope.PerConversation => context.Conversation.Address,
            _ => "*",
        };

        var tracker = context.Services.GetRequiredService<ICooldownTracker>();
        return ValueTask.FromResult(tracker.TryEnter($"{context.Command.FullName}|{Scope}|{scopeKey}", Period, out var remaining)
            ? PreconditionResult.Success
            : Fail(context, TextKey.Cooldown, Math.Ceiling(remaining.TotalSeconds)));
    }
}

/// <summary>Stores cooldown expirations. Replace it (e.g. with a distributed cache) when running several bot instances.</summary>
public interface ICooldownTracker
{
    /// <summary>Starts a cooldown for <paramref name="key"/> unless one is active.</summary>
    /// <param name="key">The cooldown key (command, scope and scope identifier).</param>
    /// <param name="period">The cooldown length.</param>
    /// <param name="remaining">The remaining time when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if no cooldown was active (a new one was started).</returns>
    bool TryEnter(string key, TimeSpan period, out TimeSpan remaining);
}

/// <summary>Thread-safe in-memory <see cref="ICooldownTracker"/> using <see cref="TimeProvider"/> (testable time).</summary>
internal sealed class CooldownTracker(TimeProvider time) : ICooldownTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _expirations = new(StringComparer.OrdinalIgnoreCase);

    public bool TryEnter(string key, TimeSpan period, out TimeSpan remaining)
    {
        var now = time.GetUtcNow();
        while (true)
        {
            if (_expirations.TryGetValue(key, out var expires))
            {
                if (expires > now)
                {
                    remaining = expires - now;
                    return false;
                }

                if (!_expirations.TryUpdate(key, now + period, expires))
                {
                    continue; // lost a race; re-evaluate
                }
            }
            else if (!_expirations.TryAdd(key, now + period))
            {
                continue; // lost a race; re-evaluate
            }

            remaining = TimeSpan.Zero;
            return true;
        }
    }
}
