using Microsoft.Extensions.Options;
using Signal.Application.Commands.Parsing;
using Signal.Application.Configuration;

namespace Signal.Application.Tests;

public class CommandParserTests
{
    private static CommandParser Parser(params string[] prefixes)
    {
        var options = new SignalOptions();
        options.Commands.Prefixes.AddRange(prefixes);
        return new CommandParser(new StaticOptionsMonitor(options));
    }

    [Theory]
    [InlineData("/ping", "/", "ping", "")]
    [InlineData("  /ping  a b ", "/", "ping", "a b")]
    [InlineData("!!ban x", "!!", "ban", "x")]
    [InlineData("!roll 20", "!", "roll", "20")]
    public void Parses_prefix_name_and_arguments(string text, string prefix, string name, string raw)
    {
        Assert.True(Parser("/", "!", "!!").TryParse(text, out var parsed));
        Assert.Equal(prefix, parsed.Prefix);
        Assert.Equal(name, parsed.Name);
        Assert.Equal(raw, parsed.RawArguments);
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("/")]
    [InlineData("/ ping")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignores_non_commands(string? text) => Assert.False(Parser().TryParse(text, out _));

    [Fact]
    public void Defaults_to_slash_prefix() => Assert.True(Parser().TryParse("/help", out _));

    [Fact]
    public void Tokenizes_quotes_and_escapes()
    {
        var tokens = CommandTokenizer.Tokenize("a \"b c\" 'd e' “f g” \"h \\\"i\\\"\" \"\"");
        Assert.Equal(["a", "b c", "d e", "f g", "h \"i\"", ""], tokens.Select(t => t.Value));
        Assert.False(tokens[0].IsQuoted);
        Assert.True(tokens[1].IsQuoted);
    }

    [Fact]
    public void Keeps_apostrophes_inside_words() =>
        Assert.Equal(["don't", "stop"], CommandTokenizer.Tokenize("don't stop").Select(t => t.Value));

    [Fact]
    public void Splits_flags_and_positional_arguments()
    {
        Assert.True(Parser().TryParse("/cmd one --name value --flag=x two --verbose -- --literal", out var parsed));
        var split = parsed.Split(new HashSet<string> { "verbose" });

        Assert.Equal(["one", "two", "--literal"], split.Positional.Select(t => t.Value));
        Assert.Equal("value", split.Flags["name"]);
        Assert.Equal("x", split.Flags["FLAG"]);
        Assert.Null(split.Flags["verbose"]);
    }

    [Fact]
    public void Quoted_flags_are_positional()
    {
        Assert.True(Parser().TryParse("/cmd \"--not-a-flag\"", out var parsed));
        Assert.Equal(["--not-a-flag"], parsed.Arguments);
        Assert.Empty(parsed.Flags);
    }

    internal sealed class StaticOptionsMonitor(SignalOptions value) : IOptionsMonitor<SignalOptions>
    {
        public SignalOptions CurrentValue => value;

        public SignalOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SignalOptions, string?> listener) => null;
    }
}
