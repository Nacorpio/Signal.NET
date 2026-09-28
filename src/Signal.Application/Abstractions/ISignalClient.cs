namespace Signal.Application.Abstractions;

/// <summary>
/// Convenience facade over all Signal ports. Inject it when you need several of them;
/// inject individual ports when you need only one (keeps dependencies explicit).
/// </summary>
public interface ISignalClient
{
    /// <summary>Sending messages.</summary>
    IMessageSender Messages { get; }

    /// <summary>Emoji reactions.</summary>
    IReactionService Reactions { get; }

    /// <summary>Read and viewed receipts.</summary>
    IReceiptService Receipts { get; }

    /// <summary>Typing indicators.</summary>
    ITypingIndicatorService Typing { get; }

    /// <summary>Group management.</summary>
    IGroupService Groups { get; }

    /// <summary>Accounts and device linking.</summary>
    IAccountService Accounts { get; }

    /// <summary>Contacts.</summary>
    IContactService Contacts { get; }

    /// <summary>Stored attachments.</summary>
    IAttachmentService Attachments { get; }

    /// <summary>Profile updates.</summary>
    IProfileService Profiles { get; }

    /// <summary>Identity keys and trust.</summary>
    IIdentityService Identities { get; }

    /// <summary>Container information and health.</summary>
    ISystemService System { get; }
}

/// <summary>Default <see cref="ISignalClient"/>: forwards to the registered ports.</summary>
internal sealed class SignalClient(
    IMessageSender messages,
    IReactionService reactions,
    IReceiptService receipts,
    ITypingIndicatorService typing,
    IGroupService groups,
    IAccountService accounts,
    IContactService contacts,
    IAttachmentService attachments,
    IProfileService profiles,
    IIdentityService identities,
    ISystemService system) : ISignalClient
{
    public IMessageSender Messages => messages;
    public IReactionService Reactions => reactions;
    public IReceiptService Receipts => receipts;
    public ITypingIndicatorService Typing => typing;
    public IGroupService Groups => groups;
    public IAccountService Accounts => accounts;
    public IContactService Contacts => contacts;
    public IAttachmentService Attachments => attachments;
    public IProfileService Profiles => profiles;
    public IIdentityService Identities => identities;
    public ISystemService System => system;
}
