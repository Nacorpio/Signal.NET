using Signal.Application.Commands;

namespace Signal.Sample.Bot.Commands;

/// <summary>Class-based command: metadata comes from the attribute, arguments are read manually.</summary>
[Command("ping", Aliases = ["p"], Description = "Checks that the bot is alive.")]
public sealed class PingCommand(TimeProvider time) : CommandBase
{
    public override async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var latency = time.GetUtcNow() - context.Envelope.ReceivedAt;
        await context.ReactAsync("🏓", cancellationToken);
        await context.ReplyAsync($"pong ({latency.TotalMilliseconds:0} ms)", cancellationToken: cancellationToken);
    }
}
