using Signal.Application.Commands;
using Signal.Application.Commands.Preconditions;
using Signal.Domain.Messaging;

namespace Signal.Sample.Bot.Commands;

/// <summary>Attribute-based commands: parameters are bound and validated from the message text.</summary>
public sealed class UtilityModule : CommandModule
{
    [Command("echo", Description = "Repeats your text.")]
    public Task EchoAsync([Remainder, Summary("The text to repeat")] string text, [Flag("upper")] bool upper) =>
        ReplyAsync(upper ? text.ToUpperInvariant() : text);

    [Command("roll", Aliases = ["dice"], Description = "Rolls a die.")]
    [Cooldown(5)]
    public Task RollAsync([Summary("Number of sides (default 6)")] int sides = 6) =>
        sides is < 2 or > 1000
            ? ReplyAsync("A die needs between 2 and 1000 sides.")
            : ReplyAsync($"🎲 {Random.Shared.Next(1, sides + 1)}");

    [Command("bold", Description = "Replies with styled text.")]
    public Task BoldAsync([Remainder] string text) =>
        Context.ReplyAsync(OutgoingMessage.To(Context.Conversation).WithStyledText($"**{text}**").Build());
}
