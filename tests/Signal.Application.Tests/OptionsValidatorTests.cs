using Signal.Application.Configuration;
using Signal.Domain;

namespace Signal.Application.Tests;

public class OptionsValidatorTests
{
    private static SignalOptions Valid() => new() { Accounts = ["+15550000000"] };

    [Fact]
    public void Accepts_defaults_with_an_account() =>
        Assert.True(new SignalOptionsValidator().Validate(null, Valid()).Succeeded);

    [Fact]
    public void Reports_all_problems()
    {
        var options = Valid();
        options.Accounts = ["not-a-number"];
        options.BaseUrl = new Uri("ftp://example.com");
        options.MaxConcurrency = 0;
        options.Commands.Prefixes = [" "];

        var result = new SignalOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures!.Count());
    }

    [Fact]
    public void Http_timeout_must_exceed_long_poll_timeout_only_when_polling()
    {
        var options = Valid();
        options.Receive.TimeoutSeconds = 60;
        Assert.True(new SignalOptionsValidator().Validate(null, options).Failed);

        options.Mode = ExecutionMode.JsonRpc;
        Assert.True(new SignalOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Requires_at_least_one_account() =>
        Assert.True(new SignalOptionsValidator().Validate(null, new SignalOptions()).Failed);
}
