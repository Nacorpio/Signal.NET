using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Pipeline;

/// <summary>
/// Per-envelope state flowing through the middleware pipeline. Each envelope gets its own DI scope,
/// so scoped services (middleware, handlers, commands, <c>DbContext</c>s, …) live exactly as long as the message.
/// </summary>
/// <param name="envelope">The received envelope.</param>
/// <param name="services">The scoped service provider of this message.</param>
/// <param name="cancellationToken">Cancelled when the host shuts down.</param>
public sealed class MessageContext(IncomingEnvelope envelope, IServiceProvider services, CancellationToken cancellationToken)
{
    /// <summary>The received envelope.</summary>
    public IncomingEnvelope Envelope { get; } = envelope;

    /// <summary>The receiving account.</summary>
    public PhoneNumber Account => Envelope.Account;

    /// <summary>The sender of the envelope.</summary>
    public Sender Sender => Envelope.Source;

    /// <summary>Where replies go: the group, or the sender for direct messages.</summary>
    public Recipient Conversation => Envelope.Conversation;

    /// <summary>The scoped service provider of this message.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Cancelled when the host shuts down.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>Arbitrary data shared between middlewares (e.g. the <c>CommandResult</c> is stored under its type).</summary>
    public IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    /// <summary>Set when a component fully handled the message (e.g. a command was executed).</summary>
    public bool IsHandled { get; set; }

    /// <summary>Sends a text into the conversation this envelope belongs to.</summary>
    /// <param name="text">The reply text.</param>
    /// <param name="quote">Quote the received data message in the reply.</param>
    /// <param name="cancellationToken">Cancels the send; defaults to <see cref="CancellationToken"/>.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(string text, bool quote = false, CancellationToken cancellationToken = default)
    {
        var builder = OutgoingMessage.To(Conversation).WithText(text);
        if (quote && Envelope.Data is { } data)
        {
            builder.Quoting(data.Timestamp, Sender.Identifier, data.Text);
        }

        return ReplyAsync(builder.Build(), cancellationToken);
    }

    /// <summary>Sends a prepared message using the account that received this envelope.</summary>
    /// <param name="message">The message; its recipients are used as-is.</param>
    /// <param name="cancellationToken">Cancels the send; defaults to <see cref="CancellationToken"/>.</param>
    /// <returns>The send result.</returns>
    public Task<SendResult> ReplyAsync(OutgoingMessage message, CancellationToken cancellationToken = default) =>
        Services.GetRequiredService<IMessageSender>().SendAsync(Account, message, Link(cancellationToken));

    /// <summary>Uses <paramref name="cancellationToken"/> if it can be cancelled, otherwise the message's token.</summary>
    internal CancellationToken Link(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled ? cancellationToken : CancellationToken;
}
