using Signal.Domain.Exceptions;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Messaging;

/// <summary>The originator of a received envelope.</summary>
/// <remarks>
/// Signal users may hide their phone number, so <see cref="Number"/> and <see cref="Uuid"/> are both optional;
/// envelopes are only created when at least one of them is known.
/// </remarks>
/// <param name="Number">The sender's phone number, if visible.</param>
/// <param name="Uuid">The sender's account identifier (ACI), if known.</param>
/// <param name="Name">The sender's profile name, if known.</param>
/// <param name="Device">The sending device id (1 = primary device).</param>
public sealed record Sender(PhoneNumber? Number, Guid? Uuid, string? Name, int Device = 1)
{
    /// <summary>The best available identifier: the phone number, otherwise the UUID.</summary>
    /// <exception cref="SignalDomainException">Neither a phone number nor a UUID is known.</exception>
    public string Identifier =>
        Number?.Value ?? Uuid?.ToString("D") ?? throw new SignalDomainException("Sender has neither a phone number nor a UUID.");

    /// <summary>Addresses this sender directly (by phone number if known, otherwise by UUID).</summary>
    /// <returns>A recipient for direct replies.</returns>
    /// <exception cref="SignalDomainException">Neither a phone number nor a UUID is known.</exception>
    public Recipient ToRecipient() =>
        Number is { } number ? number
        : Uuid is { } uuid ? new AccountId(uuid)
        : throw new SignalDomainException("Sender has neither a phone number nor a UUID.");

    /// <summary>Whether <paramref name="identifier"/> identifies this sender.</summary>
    /// <param name="identifier">A phone number in any format accepted by <see cref="PhoneNumber.TryParse(string?, out PhoneNumber)"/>, or a UUID.</param>
    /// <returns><see langword="true"/> if the phone number or UUID matches.</returns>
    public bool Matches(string identifier) =>
        (Number is { } n && PhoneNumber.TryParse(identifier, out var parsed) && parsed == n)
        || (Uuid is { } u && Guid.TryParse(identifier, out var id) && id == u);

    /// <summary>Formats the sender for logs, e.g. <c>Alice (+4915112345678)</c>.</summary>
    /// <returns>The display form of the sender.</returns>
    public override string ToString() => Name is null ? Identifier : $"{Name} ({Identifier})";
}
