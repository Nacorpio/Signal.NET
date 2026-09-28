using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Abstractions;
using Signal.Application.Commands.Parsing;
using Signal.Application.Configuration;
using Signal.Application.Pipeline;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Commands;

/// <summary>Everything a command needs: the message, parsed arguments, the scoped services and reply helpers.</summary>
/// <param name="message">The pipeline context of the triggering message.</param>
/// <param name="parsed">The parsed invocation.</param>
/// <param name="command">The matched command.</param>
public sealed class CommandContext(MessageContext message, ParsedCommand parsed, CommandDescriptor command)
{
    /// <summary>The pipeline context of the triggering message.</summary>
    public MessageContext Message { get; } = message;

    /// <summary>The parsed invocation (prefix, name, raw arguments, tokens).</summary>
    public ParsedCommand Parsed { get; } = parsed;

    /// <summary>The matched command.</summary>
    public CommandDescriptor Command { get; } = command;

    /// <summary>The triggering envelope.</summary>
    public IncomingEnvelope Envelope => Message.Envelope;

    /// <summary>The triggering data message.</summary>
    /// <exception cref="InvalidOperationException">The envelope has no data message (never the case for parsed commands).</exception>
    public DataMessage Data => Envelope.Data ?? throw new InvalidOperationException("Commands require a data message.");

    /// <summary>The receiving account.</summary>
    public PhoneNumber Account => Message.Account;

    /// <summary>Who invoked the command.</summary>
    public Sender Sender => Message.Sender;

    /// <summary>Where replies go: the group, or the sender for direct messages.</summary>
    public Recipient Conversation => Message.Conversation;

    /// <summary>Whether the command was sent in a group.</summary>
    public bool IsGroup => Envelope.IsGroup;

    /// <summary>The group the command was sent in, or <see langword="null"/>.</summary>
    public GroupId? Group => Envelope.Group;

    /// <summary>The message's scoped service provider.</summary>
    public IServiceProvider Services => Message.Services;

    /// <summary>Cancelled when the host shuts down.</summary>
    public CancellationToken CancellationToken => Message.CancellationToken;

    /// <summary>Positional arguments (naive split: every flag consumes the following token; for <see cref="ICommand"/> implementations).</summary>
    public IReadOnlyList<string> Arguments => Parsed.Arguments;

    /// <summary>Options given as <c>--name value</c>, <c>--name=value</c> or <c>--name</c> (null value); names are case-insensitive.</summary>
    public IReadOnlyDictionary<string, string?> Flags => Parsed.Flags;

    /// <summary>Replies into the conversation.</summary>
    /// <param name="text">The reply text.</param>
    /// <param name="quote">Quote the triggering message; defaults to <see cref="CommandOptions.QuoteReplies"/>.</param>
    /// <param name="cancellationToken">Cancels the send; defaults to <see cref="CancellationToken"/>.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(string text, bool? quote = null, CancellationToken cancellationToken = default)
    {
        var quoteReply = quote ?? Services.GetRequiredService<IOptionsMonitor<SignalOptions>>().CurrentValue.Commands.QuoteReplies;
        return Message.ReplyAsync(text, quoteReply, cancellationToken);
    }

    /// <summary>Replies using Signal's text styling (<c>**bold**</c>, <c>*italic*</c>, …).</summary>
    /// <param name="text">The styled reply text.</param>
    /// <param name="cancellationToken">Cancels the send; defaults to <see cref="CancellationToken"/>.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyStyledAsync(string text, CancellationToken cancellationToken = default) =>
        Message.ReplyAsync(OutgoingMessage.To(Conversation).WithStyledText(text).Build(), cancellationToken);

    /// <summary>Sends a prepared message (e.g. with attachments) from the receiving account.</summary>
    /// <param name="message">The message; typically addressed to <see cref="Conversation"/>.</param>
    /// <param name="cancellationToken">Cancels the send; defaults to <see cref="CancellationToken"/>.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(OutgoingMessage message, CancellationToken cancellationToken = default) =>
        Message.ReplyAsync(message, cancellationToken);

    /// <summary>Reacts to the triggering message.</summary>
    /// <param name="emoji">The reaction emoji.</param>
    /// <param name="cancellationToken">Cancels the request; defaults to <see cref="CancellationToken"/>.</param>
    /// <returns>A task that completes when the reaction was sent.</returns>
    public Task ReactAsync(string emoji, CancellationToken cancellationToken = default) =>
        Services.GetRequiredService<IReactionService>()
            .SendReactionAsync(Account, Conversation, emoji, Sender.Identifier, Data.Timestamp, Message.Link(cancellationToken));
}
