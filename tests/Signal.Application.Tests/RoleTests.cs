using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signal.Application.Commands;
using Signal.Application.Configuration;
using Signal.Application.Pipeline;
using Signal.Application.Roles;

namespace Signal.Application.Tests;

public sealed class RoleModule : CommandModule
{
    [Command("mod"), RequireRole("moderator")]
    public Task ModAsync() => ReplyAsync("mod ok");

    [Command("admin"), RequireRole(Role.Admin)]
    public Task AdminAsync() => ReplyAsync("admin ok");

    [Command("ban"), RequireRole("moderator", Role.GroupAdmin)]
    public Task BanAsync() => ReplyAsync("ban ok");

    [Command("vip"), RequireRole("vip")]
    public Task VipAsync() => ReplyAsync("vip ok");

    [Command("quiet"), RequireRole("moderator", ErrorMessage = "")]
    public Task QuietAsync() => ReplyAsync("quiet ok");
}

/// <summary>A custom provider, e.g. what a database-backed one would look like.</summary>
public sealed class VipRoleProvider : IRoleProvider
{
    public ValueTask<bool> HasRoleAsync(MessageContext message, string role, CancellationToken cancellationToken) =>
        ValueTask.FromResult(role == "vip" && message.Sender.Matches(RoleTests.Bob));
}

public class RoleTests
{
    public const string Bob = "+15550002222";

    private static TestHarness Harness(Action<SignalOptions>? configure = null, bool withVip = false) => TestHarness.Create(
        o =>
        {
            o.Commands.Roles = new() { ["Moderator"] = [TestHarness.Alice] };
            configure?.Invoke(o);
        },
        (services, catalog) =>
        {
            catalog.AddModule(typeof(RoleModule));
            if (withVip)
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IRoleProvider, VipRoleProvider>());
            }
        });

    [Theory]
    [InlineData("/mod", TestHarness.Alice, "mod ok")]
    [InlineData("/mod", Bob, "This command requires the moderator role.")]
    [InlineData("/ban", Bob, "This command requires one of these roles: moderator, group-admin.")]
    public async Task Configured_roles_are_granted_case_insensitively(string text, string from, string expected)
    {
        await using var harness = Harness();

        await harness.ReceiveAsync(text, from);

        Assert.Equal(expected, harness.Signal.LastReply);
    }

    [Fact]
    public async Task Admins_have_the_admin_role()
    {
        await using var harness = Harness(o => o.Commands.Admins = [Bob]);

        await harness.ReceiveAsync("/admin", Bob);
        var bob = harness.Signal.LastReply;
        await harness.ReceiveAsync("/admin");

        Assert.Equal("admin ok", bob);
        Assert.Equal("This command requires the admin role.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Group_admins_have_the_group_admin_role_only_in_their_group()
    {
        await using var harness = Harness();
        harness.Signal.GroupAdmins.Add(Bob);

        await harness.ReceiveAsync("/ban", Bob, inGroup: true);
        var inGroup = harness.Signal.LastReply;
        await harness.ReceiveAsync("/ban", Bob);

        Assert.Equal("ban ok", inGroup);
        Assert.StartsWith("This command requires one of these roles", harness.Signal.LastReply);
    }

    [Fact]
    public async Task Custom_providers_are_consulted()
    {
        await using var harness = Harness(withVip: true);

        await harness.ReceiveAsync("/vip", Bob);
        var bob = harness.Signal.LastReply;
        await harness.ReceiveAsync("/vip");

        Assert.Equal("vip ok", bob);
        Assert.Equal("This command requires the vip role.", harness.Signal.LastReply);
    }

    [Fact]
    public async Task An_empty_error_message_fails_silently()
    {
        await using var harness = Harness();

        await harness.ReceiveAsync("/quiet", Bob);

        Assert.Empty(harness.Signal.Sent);
    }

    [Fact]
    public void RequireRole_needs_a_role() => Assert.Throws<ArgumentException>(() => new RequireRoleAttribute());
}
