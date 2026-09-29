using Signal.Application.Abstractions;

namespace Signal.Application.Tests;

public class CapabilityTests
{
    private static SignalApiInfo Info(Dictionary<string, IReadOnlyList<string>> capabilities) =>
        new("0.92", 2, "json-rpc", ["v1", "v2"], capabilities);

    [Fact]
    public void Supports_matches_endpoint_and_feature_ignoring_feature_case()
    {
        var info = Info(new() { ["v2/send"] = ["quotes", "Mentions"] });

        Assert.True(info.Supports(SignalCapability.SendQuotes));
        Assert.True(info.Supports(SignalCapability.SendMentions));
        Assert.False(info.Supports(new SignalCapability("v2/send", "stickers")));
        Assert.False(info.Supports(new SignalCapability("v1/receive", "quotes")));
    }

    [Fact]
    public void EnsureSupported_names_the_missing_capability_and_version()
    {
        var info = Info([]);

        var error = Assert.Throws<NotSupportedException>(() => info.EnsureSupported(SignalCapability.SendMentions));

        Assert.Contains("'mentions'", error.Message);
        Assert.Contains("v2/send", error.Message);
        Assert.Contains("0.92", error.Message);
    }

    [Fact]
    public void Default_capability_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Info([]).Supports(default));
        Assert.Equal("v2/send:quotes", SignalCapability.SendQuotes.ToString());
    }
}
