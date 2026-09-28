using Microsoft.Extensions.Logging;
using Signal.Application.Abstractions;
using Signal.Application.Events;
using Signal.Domain.Events;
using Signal.Domain.Messaging;

namespace Signal.Sample.Bot.Handlers;

/// <summary>Logs every reaction that is added or removed (demonstrates <see cref="IEventHandler{TEvent}"/>).</summary>
public sealed partial class ReactionLogger(ILogger<ReactionLogger> logger) : IEventHandler<ReactionReceived>
{
    public Task HandleAsync(ReactionReceived domainEvent, CancellationToken cancellationToken)
    {
        LogReaction(domainEvent.Envelope.Source.ToString(), domainEvent.Reaction.IsRemove ? "removed" : "added", domainEvent.Reaction.Emoji);
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "{Sender} {Action} reaction {Emoji}")]
    private partial void LogReaction(string sender, string action, string emoji);
}

/// <summary>Greets a group when it changes (e.g. the bot was added).</summary>
public sealed class WelcomeHandler(IMessageSender sender) : IEventHandler<GroupUpdated>
{
    public Task HandleAsync(GroupUpdated domainEvent, CancellationToken cancellationToken) =>
        sender.SendAsync(
            domainEvent.Envelope.Account,
            OutgoingMessage.To(domainEvent.Group).WithText("👋 Hi! Send /help to see what I can do.").Build(),
            cancellationToken);
}
