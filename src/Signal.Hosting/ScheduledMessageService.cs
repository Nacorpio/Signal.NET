using Microsoft.Extensions.Hosting;
using Signal.Application.Scheduling;

namespace Signal.Hosting;

/// <summary>Hosts the <see cref="IScheduledMessageDispatcher"/>, so scheduled messages are sent when due.</summary>
/// <param name="dispatcher">The dispatcher.</param>
internal sealed class ScheduledMessageService(IScheduledMessageDispatcher dispatcher) : BackgroundService
{
    /// <summary>Runs the dispatcher until the host stops.</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => dispatcher.RunAsync(stoppingToken);
}
