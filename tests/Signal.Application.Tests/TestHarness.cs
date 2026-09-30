using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Application.Commands;
using Signal.Application.Configuration;
using Signal.Application.Pipeline;
using Signal.Domain.Entities;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Tests;

/// <summary>Application layer wired up with in-memory fakes for the infrastructure ports.</summary>
internal sealed class TestHarness : IAsyncDisposable
{
    public const string Account = "+15550000000";
    public const string Alice = "+15550001111";
    public static readonly GroupId Group = GroupId.FromInternalId("test-group");

    private readonly ServiceProvider _provider;
    private long _timestamp = 1_700_000_000_000;

    private TestHarness(ServiceProvider provider) => _provider = provider;

    public FakeSignal Signal => _provider.GetRequiredService<FakeSignal>();

    public IServiceProvider Services => _provider;

    public static TestHarness Create(Action<SignalOptions>? configure = null, Action<IServiceCollection, CommandCatalog>? setup = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<SignalOptions>().Configure(o =>
        {
            o.Accounts = [Account];
            configure?.Invoke(o);
        });
        services.AddSignalApplication();

        services.AddSingleton<FakeSignal>();
        services.AddSingleton<IMessageSender>(sp => sp.GetRequiredService<FakeSignal>());
        services.AddSingleton<IReactionService>(sp => sp.GetRequiredService<FakeSignal>());
        services.AddSingleton<IGroupService>(sp => sp.GetRequiredService<FakeSignal>());

        setup?.Invoke(services, services.GetOrAddSingletonInstance<CommandCatalog>());
        return new TestHarness(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }

    /// <summary>Runs a text message through the full pipeline and returns the context.</summary>
    public async Task<MessageContext> ReceiveAsync(string text, string from = Alice, bool inGroup = false)
    {
        var data = new DataMessage(Interlocked.Increment(ref _timestamp), text) { Group = inGroup ? Group : null };
        return await ReceiveAsync(new IncomingEnvelope(PhoneNumber.Parse(Account), new Sender(PhoneNumber.Parse(from), null, "Test"), data.Timestamp, data));
    }

    public async Task<MessageContext> ReceiveAsync(IncomingEnvelope envelope)
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = new MessageContext(envelope, scope.ServiceProvider, CancellationToken.None);
        await _provider.GetRequiredService<IMessagePipeline>().ExecuteAsync(context);
        return context;
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

internal sealed class FakeSignal : IMessageSender, IReactionService, IGroupService
{
    public ConcurrentQueue<OutgoingMessage> Sent { get; } = new();

    public ConcurrentQueue<string> Reactions { get; } = new();

    public HashSet<string> GroupAdmins { get; } = [];

    public string? LastReply => Sent.LastOrDefault()?.Text;

    /// <summary>Runs inside SendAsync, e.g. to simulate an instant answer.</summary>
    public Action<OutgoingMessage>? OnSend { get; set; }

    public Task<SendResult> SendAsync(PhoneNumber account, OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(message);
        OnSend?.Invoke(message);
        return Task.FromResult(new SendResult(Sent.Count));
    }

    public Task SendReactionAsync(PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default)
    {
        Reactions.Enqueue(emoji);
        return Task.CompletedTask;
    }

    public Task RemoveReactionAsync(PhoneNumber account, Recipient recipient, string emoji, string targetAuthor, long targetTimestamp, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<Group?> GetAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) =>
        Task.FromResult<Group?>(new Group(group, "Test", [TestHarness.Alice], GroupAdmins));

    public Task<IReadOnlyList<Group>> ListAsync(PhoneNumber account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<GroupId> CreateAsync(PhoneNumber account, string name, IEnumerable<string> members, string? description = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(PhoneNumber account, GroupId group, string? name = null, string? description = null, string? base64Avatar = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddMembersAsync(PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RemoveMembersAsync(PhoneNumber account, GroupId group, IEnumerable<string> members, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddAdminsAsync(PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RemoveAdminsAsync(PhoneNumber account, GroupId group, IEnumerable<string> admins, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task QuitAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(PhoneNumber account, GroupId group, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
