namespace Signal.Domain.Exceptions;

/// <summary>
/// Raised when a domain invariant is violated, for example when an <see cref="Messaging.OutgoingMessage"/>
/// has no recipient. Base class of all domain exceptions.
/// </summary>
/// <param name="message">Description of the violated invariant.</param>
/// <param name="innerException">The underlying cause, if any.</param>
public class SignalDomainException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Raised when a string is not a valid E.164 phone number.</summary>
/// <param name="value">The rejected input.</param>
public sealed class InvalidPhoneNumberException(string? value)
    : SignalDomainException($"'{value}' is not a valid E.164 phone number (expected e.g. +4915112345678).")
{
    /// <summary>The rejected input.</summary>
    public string? Value { get; } = value;
}

/// <summary>Raised when a string cannot be interpreted as a recipient or group id.</summary>
/// <param name="value">The rejected input.</param>
/// <param name="reason">Why the input was rejected.</param>
public sealed class InvalidRecipientException(string? value, string reason)
    : SignalDomainException($"'{value}' is not a valid recipient: {reason}")
{
    /// <summary>The rejected input.</summary>
    public string? Value { get; } = value;
}
