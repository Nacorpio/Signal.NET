using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.Entities;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

public class GroupContactExtrasTests
{
    private static readonly PhoneNumber Account = PhoneNumber.Parse(TestServices.Account);
    private static readonly GroupId Group = GroupId.FromInternalId("g");
    private const string GroupPath = "/v1/groups/%2B15550000000/group.Zw%3D%3D";

    private static (StubHandler Handler, ServiceProvider Provider) Build(Func<HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler((_, _) => respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NoContent));
        return (handler, TestServices.Build(handler, o => o.Http.RetryCount = 0));
    }

    [Fact]
    public async Task Join_and_block_post_to_their_endpoints()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var groups = provider.GetRequiredService<IGroupService>();

        await groups.JoinAsync(Account, Group);
        await groups.BlockAsync(Account, Group);

        Assert.Equal(
            [(HttpMethod.Post, $"{GroupPath}/join"), (HttpMethod.Post, $"{GroupPath}/block")],
            handler.Requests.Select(r => (r.Method, r.PathAndQuery)));
    }

    [Fact]
    public async Task UpdateSettings_sends_only_the_changed_settings()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var groups = provider.GetRequiredService<IGroupService>();

        await groups.UpdateSettingsAsync(Account, Group, new GroupSettings(
            new GroupPermissions(GroupPermission.EveryMember, GroupPermission.OnlyAdmins, GroupPermission.OnlyAdmins),
            GroupLinkMode.EnabledWithApproval,
            TimeSpan.FromHours(1)));
        await groups.UpdateSettingsAsync(Account, Group, new GroupSettings(Link: GroupLinkMode.Disabled));

        var requests = handler.Requests.ToArray();
        Assert.Equal(HttpMethod.Put, requests[0].Method);
        Assert.Equal(GroupPath, requests[0].PathAndQuery);
        Assert.Equal(
            """{"permissions":{"add_members":"every-member","edit_group":"only-admins","send_messages":"only-admins"},"group_link":"enabled-with-approval","expiration_time":3600}""",
            requests[0].Body);
        Assert.Equal("""{"group_link":"disabled"}""", requests[1].Body);
    }

    [Fact]
    public async Task UpdateSettings_without_changes_or_with_negative_timer_makes_no_request()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var groups = provider.GetRequiredService<IGroupService>();

        await groups.UpdateSettingsAsync(Account, Group, new GroupSettings());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            groups.UpdateSettingsAsync(Account, Group, new GroupSettings(MessageExpiration: TimeSpan.FromSeconds(-1))));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Pin_and_unpin_send_author_and_timestamp()
    {
        var (handler, provider) = Build(() => new HttpResponseMessage(HttpStatusCode.OK));
        await using var _ = provider;
        var groups = provider.GetRequiredService<IGroupService>();

        await groups.PinMessageAsync(Account, Group, " +15550001111 ", 42, TimeSpan.FromDays(1));
        await groups.PinMessageAsync(Account, Group, "+15550001111", 43);
        await groups.UnpinMessageAsync(Account, Group, "+15550001111", 42);

        var requests = handler.Requests.ToArray();
        Assert.All(requests, r => Assert.Equal($"{GroupPath}/pin-message", r.PathAndQuery));
        Assert.Equal(("+15550001111", 42L, (int?)86400), Pin(requests[0].Body));
        Assert.Equal(("+15550001111", 43L, (int?)null), Pin(requests[1].Body));
        Assert.Equal(HttpMethod.Delete, requests[2].Method);
        Assert.Equal(("+15550001111", 42L, (int?)null), Pin(requests[2].Body));

        // Compare parsed values: the serializer escapes '+' as +, which is equivalent JSON.
        static (string?, long, int?) Pin(string? body)
        {
            var root = JsonDocument.Parse(body!).RootElement;
            return (root.GetProperty("target_author").GetString(), root.GetProperty("timestamp").GetInt64(),
                root.TryGetProperty("duration", out var d) ? d.GetInt32() : null);
        }
    }

    [Fact]
    public async Task Pin_rejects_invalid_arguments_without_calling_the_api()
    {
        var (handler, provider) = Build();
        await using var _ = provider;
        var groups = provider.GetRequiredService<IGroupService>();

        await Assert.ThrowsAsync<ArgumentException>(() => groups.PinMessageAsync(Account, Group, " ", 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => groups.PinMessageAsync(Account, Group, "a", 1, TimeSpan.FromMilliseconds(500)));
        await Assert.ThrowsAsync<ArgumentException>(() => groups.UnpinMessageAsync(Account, Group, "", 1));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Contact_sync_posts_to_sync()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IContactService>().SyncAsync(Account);

        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "/v1/contacts/%2B15550000000/sync"), (request.Method, request.PathAndQuery));
    }

    [Fact]
    public async Task CheckRegistered_repeats_the_query_parameter_and_maps_results()
    {
        var (handler, provider) = Build(() => StubHandler.Json("""
            [{"number":"+15550001111","registered":true},{"number":"+15550002222","registered":false},{"number":"garbage","registered":true}]
            """));
        await using var _ = provider;
        var alice = PhoneNumber.Parse("+15550001111");
        var bob = PhoneNumber.Parse("+15550002222");

        var results = await provider.GetRequiredService<IContactService>().CheckRegisteredAsync(Account, [alice, bob, alice]);

        Assert.Equal("/v1/search/%2B15550000000?numbers=%2B15550001111&numbers=%2B15550002222", Assert.Single(handler.Requests).PathAndQuery);
        Assert.Equal([new NumberRegistration(alice, true), new NumberRegistration(bob, false)], results);
    }

    [Fact]
    public async Task CheckRegistered_with_no_numbers_makes_no_request()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        Assert.Empty(await provider.GetRequiredService<IContactService>().CheckRegisteredAsync(Account, []));
        Assert.Empty(handler.Requests);
    }

    /// <summary>Custom services written before these members existed.</summary>
    private sealed class LegacyContactService : IContactService
    {
        public Task<IReadOnlyList<Contact>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Contact>>([]);

        public Task UpdateAsync(PhoneNumber account, Recipient contact, string? name, int? expirationInSeconds = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task Existing_contact_implementations_report_unsupported()
    {
        IContactService legacy = new LegacyContactService();

        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.SyncAsync(Account));
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.CheckRegisteredAsync(Account, [Account]));
    }
}
