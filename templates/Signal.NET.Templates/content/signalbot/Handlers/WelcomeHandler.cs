using Signal.Application.Abstractions;
using Signal.Application.Events;
using Signal.Domain.Events;
using Signal.Domain.Messaging;

namespace SignalBot.Handlers;

/// <summary>Example event handler: greets a group when it changes (e.g. the bot was added).</summary>
public sealed class WelcomeHandler(IMessageSender sender) : IEventHandler<GroupUpdated>
{
    public Task HandleAsync(GroupUpdated domainEvent, CancellationToken cancellationToken) =>
        sender.SendAsync(
            domainEvent.Envelope.Account,
            OutgoingMessage.To(domainEvent.Group).WithText("👋 Hi! Send SIGNAL_PREFIXhelp to see what I can do.").Build(),
            cancellationToken);
}
