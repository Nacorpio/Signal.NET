using Microsoft.Extensions.Configuration;
using Signal.Application.Configuration;

namespace Signal.Infrastructure.Tests;

public class RoleConfigurationTests
{
    [Fact]
    public void Roles_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Signal:Commands:Roles:moderator:0"] = "+15550001111",
                ["Signal:Commands:Roles:moderator:1"] = "8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f",
                ["Signal:Commands:Roles:helper:0"] = "+15550002222",
            })
            .Build();

        var options = configuration.GetSection(SignalOptions.SectionName).Get<SignalOptions>()!;

        Assert.Equal(["+15550001111", "8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f"], options.Commands.Roles["moderator"]);
        Assert.Equal(["+15550002222"], options.Commands.Roles["helper"]);
    }
}
