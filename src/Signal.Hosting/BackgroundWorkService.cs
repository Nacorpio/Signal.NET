using Microsoft.Extensions.Hosting;
using Signal.Application.Background;

namespace Signal.Hosting;

/// <summary>Hosts the <see cref="IBackgroundWorkProcessor"/>, so work queued by commands and handlers runs.</summary>
/// <param name="processor">The processor.</param>
internal sealed class BackgroundWorkService(IBackgroundWorkProcessor processor) : BackgroundService
{
    /// <summary>Runs the processor until the host stops.</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => processor.RunAsync(stoppingToken);
}
