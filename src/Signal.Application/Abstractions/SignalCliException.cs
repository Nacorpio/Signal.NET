namespace Signal.Application.Abstractions;

/// <summary>signal-cli's JSON-RPC daemon refused a request (e.g. an invalid or reset group invite link).</summary>
/// <param name="method">The JSON-RPC method, e.g. <c>joinGroup</c>.</param>
/// <param name="code">The JSON-RPC error code.</param>
/// <param name="error">signal-cli's error message.</param>
public sealed class SignalCliException(string method, int code, string? error)
    : Exception($"signal-cli '{method}' failed with code {code}: {error ?? "no details"}")
{
    /// <summary>The JSON-RPC method.</summary>
    public string Method { get; } = method;

    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; } = code;

    /// <summary>signal-cli's error message, if any.</summary>
    public string? Error { get; } = error;
}
