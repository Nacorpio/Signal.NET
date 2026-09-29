using Signal.Application.Commands;
using Signal.Application.Commands.Preconditions;

namespace SignalBot.Commands;

/// <summary>Example commands. Add methods with [Command] or new CommandModule classes; they are discovered automatically.</summary>
public sealed class GeneralModule(TimeProvider time) : CommandModule
{
    [Command("ping", Description = "Checks that the bot is alive.")]
    public async Task PingAsync()
    {
        var latency = time.GetUtcNow() - Context.Envelope.ReceivedAt;
        await ReactAsync("🏓");
        await ReplyAsync($"pong ({latency.TotalMilliseconds:0} ms)");
    }

    [Command("echo", Description = "Repeats your text.")]
    public Task EchoAsync([Remainder, Summary("The text to repeat")] string text) => ReplyAsync(text);

    [Command("roll", Description = "Rolls a die."), Cooldown(5)]
    public Task RollAsync([Summary("Number of sides")] int sides = 6) =>
        sides is < 2 or > 1000
            ? ReplyAsync("A die needs between 2 and 1000 sides.")
            : ReplyAsync($"🎲 {Random.Shared.Next(1, sides + 1)}");

    [Command("whoami", Description = "Shows how the bot sees you."), RequireAdmin]
    public Task WhoAmIAsync() => ReplyAsync($"{Context.Sender} in {Context.Conversation}");
}
