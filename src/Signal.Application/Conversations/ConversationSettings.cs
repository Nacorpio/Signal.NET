using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Commands;
using Signal.Application.Pipeline;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Conversations;

/// <summary>
/// Settings for one conversation (a group or a direct chat), overriding the global command options there.
/// <see langword="null"/> values fall back to the global configuration.
/// </summary>
public sealed record ConversationSettings
{
    /// <summary>Command prefixes in this conversation instead of <c>Signal:Commands:Prefixes</c>, e.g. <c>["!"]</c>.</summary>
    public IReadOnlyList<string>? Prefixes { get; init; }

    /// <summary>The conversation's language as a culture name (e.g. <c>de-DE</c>), for localised replies.</summary>
    public string? Culture { get; init; }

    /// <summary>
    /// Commands that can't be used in this conversation, by full name (<c>ban</c>, <c>playlist add</c>) or group name
    /// (<c>playlist</c> disables the whole group). Case-insensitive.
    /// </summary>
    public IReadOnlyList<string> DisabledCommands { get; init; } = [];

    /// <summary>Whether <paramref name="command"/> is disabled here, by its full name or its group.</summary>
    /// <param name="command">The command.</param>
    /// <returns><see langword="true"/> if the command must not run in this conversation.</returns>
    public bool IsDisabled(CommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return DisabledCommands.Any(d =>
            string.Equals(d, command.FullName, StringComparison.OrdinalIgnoreCase)
            || (command.Group is { } group && string.Equals(d, group.Name, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>
/// Stores per-conversation settings. The default keeps them in memory (lost on restart); implement this port with a
/// database to keep them. It is queried at most once per text message, so keep reads cheap (or cache).
/// </summary>
public interface IConversationSettingsStore
{
    /// <summary>Gets a conversation's settings.</summary>
    /// <param name="account">The receiving account.</param>
    /// <param name="conversation">The group or direct-chat partner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The settings, or <see langword="null"/> if none are stored (global configuration applies).</returns>
    ValueTask<ConversationSettings?> GetAsync(PhoneNumber account, Recipient conversation, CancellationToken cancellationToken = default);

    /// <summary>Stores a conversation's settings, replacing earlier ones.</summary>
    /// <param name="account">The receiving account.</param>
    /// <param name="conversation">The group or direct-chat partner.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the settings are stored.</returns>
    /// <exception cref="ArgumentException">A prefix is empty or contains whitespace.</exception>
    Task SetAsync(PhoneNumber account, Recipient conversation, ConversationSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Removes a conversation's settings, so the global configuration applies again.</summary>
    /// <param name="account">The receiving account.</param>
    /// <param name="conversation">The group or direct-chat partner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> if settings existed.</returns>
    Task<bool> RemoveAsync(PhoneNumber account, Recipient conversation, CancellationToken cancellationToken = default);
}

/// <summary>Reads the settings of a message's conversation.</summary>
public static class ConversationSettingsExtensions
{
    /// <summary>
    /// The settings of this message's conversation, loaded once per message and cached in
    /// <see cref="MessageContext.Items"/>.
    /// </summary>
    /// <param name="context">The message.</param>
    /// <returns>The settings, or <see langword="null"/> if the conversation has none.</returns>
    public static async ValueTask<ConversationSettings?> GetConversationSettingsAsync(this MessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.TryGetValue(typeof(ConversationSettings), out var cached))
        {
            return (ConversationSettings?)cached;
        }

        var settings = await context.Services.GetRequiredService<IConversationSettingsStore>()
            .GetAsync(context.Account, context.Conversation, context.CancellationToken);
        context.Items[typeof(ConversationSettings)] = settings;
        return settings;
    }
}

/// <summary>Thread-safe in-memory store keyed by account and conversation address.</summary>
internal sealed class InMemoryConversationSettingsStore : IConversationSettingsStore
{
    private readonly ConcurrentDictionary<(PhoneNumber, string), ConversationSettings> _settings = new();

    public ValueTask<ConversationSettings?> GetAsync(PhoneNumber account, Recipient conversation, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_settings.TryGetValue((account, conversation.Address), out var settings) ? settings : null);

    public Task SetAsync(PhoneNumber account, Recipient conversation, ConversationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Prefixes?.FirstOrDefault(p => string.IsNullOrEmpty(p) || p.Any(char.IsWhiteSpace)) is { } invalid)
        {
            throw new ArgumentException($"Prefix '{invalid}' must not be empty or contain whitespace.", nameof(settings));
        }

        _settings[(account, conversation.Address)] = settings;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(PhoneNumber account, Recipient conversation, CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings.TryRemove((account, conversation.Address), out _));
}
