namespace Signal.Domain;

/// <summary>
/// Execution mode of the signal-cli-rest-api container, i.e. the value of its <c>MODE</c> environment variable.
/// </summary>
/// <remarks>
/// The mode decides how Signal.NET receives messages: the <see cref="Normal"/> and <see cref="Native"/> modes expose
/// <c>GET /v1/receive/{number}</c> as a plain HTTP endpoint that must be polled, whereas the
/// <see cref="JsonRpc"/> and <see cref="JsonRpcNative"/> modes upgrade the same endpoint to a WebSocket that pushes
/// envelopes as they arrive. Sending and all other endpoints behave identically in every mode.
/// The configured mode must match the container; see <c>SignalOptions.Mode</c>.
/// </remarks>
public enum ExecutionMode
{
    /// <summary><c>normal</c>: signal-cli is started (JVM) for every request. Slowest; messages are received by HTTP polling.</summary>
    Normal,

    /// <summary><c>native</c>: like <see cref="Normal"/>, but uses the GraalVM native image of signal-cli (faster start-up). Messages are received by HTTP polling.</summary>
    Native,

    /// <summary><c>json-rpc</c>: a long-running signal-cli daemon (JVM). Fastest; messages are pushed over a WebSocket.</summary>
    JsonRpc,

    /// <summary><c>json-rpc-native</c>: the JSON-RPC daemon running as a GraalVM native image. Messages are pushed over a WebSocket.</summary>
    JsonRpcNative,
}

/// <summary>Derived properties and parsing helpers for <see cref="ExecutionMode"/>.</summary>
public static class ExecutionModeExtensions
{
    /// <param name="mode">The execution mode being described.</param>
    extension(ExecutionMode mode)
    {
        /// <summary>
        /// Whether messages are pushed by the container over a WebSocket (<c>json-rpc*</c> modes)
        /// instead of being polled over HTTP.
        /// </summary>
        public bool IsStreaming => mode is ExecutionMode.JsonRpc or ExecutionMode.JsonRpcNative;

        /// <summary>Whether the container runs the GraalVM native image of signal-cli (<c>*native</c> modes).</summary>
        public bool IsNative => mode is ExecutionMode.Native or ExecutionMode.JsonRpcNative;

        /// <summary>The value of the container's <c>MODE</c> environment variable, e.g. <c>json-rpc</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ExecutionMode"/>.</exception>
        public string ContainerValue => mode switch
        {
            ExecutionMode.Normal => "normal",
            ExecutionMode.Native => "native",
            ExecutionMode.JsonRpc => "json-rpc",
            ExecutionMode.JsonRpcNative => "json-rpc-native",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    /// <summary>
    /// Parses a container <c>MODE</c> value such as <c>json-rpc</c> or <c>json-rpc-native</c>.
    /// Enum names such as <c>JsonRpc</c> are accepted as well; parsing is case-insensitive.
    /// </summary>
    /// <param name="value">The text to parse, e.g. the <c>mode</c> reported by <c>GET /v1/about</c>.</param>
    /// <param name="mode">The parsed mode when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> denotes a known mode.</returns>
    public static bool TryParseContainerValue(string? value, out ExecutionMode mode)
    {
        mode = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // "json-rpc-native" → "jsonrpcnative", which Enum.TryParse matches case-insensitively to JsonRpcNative.
        var normalized = value.Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return Enum.TryParse(normalized, ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }
}
