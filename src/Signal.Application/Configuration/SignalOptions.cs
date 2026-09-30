using Signal.Domain;

namespace Signal.Application.Configuration;

/// <summary>
/// Root options of Signal.NET, bound from the <c>Signal</c> section of <c>appsettings.json</c>
/// and validated on startup by <see cref="SignalOptionsValidator"/>.
/// </summary>
/// <remarks>
/// Settings read through <c>IOptionsMonitor</c> (commands, access control, rate limit, receive options)
/// apply without a restart when the configuration file changes. Connection-level settings (base URL, mode,
/// accounts, HTTP resilience, concurrency) are read once at startup.
/// </remarks>
public sealed class SignalOptions
{
    /// <summary>The configuration section name: <c>Signal</c>.</summary>
    public const string SectionName = "Signal";

    /// <summary>Base URL of the signal-cli-rest-api container. Default <c>http://localhost:8080/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8080/");

    /// <summary>
    /// Must match the container's <c>MODE</c>. Decides between HTTP polling (<see cref="ExecutionMode.Normal"/>,
    /// <see cref="ExecutionMode.Native"/>) and WebSocket receiving (<see cref="ExecutionMode.JsonRpc"/>,
    /// <see cref="ExecutionMode.JsonRpcNative"/>). Default <see cref="ExecutionMode.Normal"/>.
    /// </summary>
    public ExecutionMode Mode { get; set; } = ExecutionMode.Normal;

    /// <summary>Registered accounts (E.164 phone numbers) to receive messages for. At least one is required.</summary>
    public List<string> Accounts { get; set; } = [];

    /// <summary>
    /// Number of conversations processed in parallel. Messages of the same conversation are always processed in order.
    /// Default 4.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Read <c>/v1/about</c> on startup and compare the container mode with <see cref="Mode"/>. Default <see langword="true"/>.</summary>
    public bool VerifyModeOnStartup { get; set; } = true;

    /// <summary>Stop the host when the verified mode differs from <see cref="Mode"/>; otherwise only a warning is logged.</summary>
    public bool FailOnModeMismatch { get; set; }

    /// <summary>HTTP polling settings (<c>normal</c>/<c>native</c> modes).</summary>
    public ReceiveOptions Receive { get; set; } = new();

    /// <summary>WebSocket settings (<c>json-rpc</c>/<c>json-rpc-native</c> modes).</summary>
    public WebSocketOptions WebSocket { get; set; } = new();

    /// <summary>HTTP client timeout and retry settings.</summary>
    public HttpOptions Http { get; set; } = new();

    /// <summary>Command system settings.</summary>
    public CommandOptions Commands { get; set; } = new();

    /// <summary>Sender allow/block lists.</summary>
    public AccessControlOptions AccessControl { get; set; } = new();

    /// <summary>Per-sender rate limiting.</summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>Background work queued by commands and handlers (<c>IBackgroundWorkQueue</c>).</summary>
    public BackgroundOptions Background { get; set; } = new();
}

/// <summary>Background work settings.</summary>
public sealed class BackgroundOptions
{
    /// <summary>How many background work items run at the same time. Default 4.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>How many items may wait in the queue; queuing waits while it is full. Default 100.</summary>
    public int Capacity { get; set; } = 100;
}

/// <summary>HTTP polling of <c>GET /v1/receive/{number}</c> (<c>normal</c> / <c>native</c> modes).</summary>
public sealed class ReceiveOptions
{
    /// <summary>Delay between polls when the previous poll returned nothing. Default 1 s.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Server-side long-poll timeout in seconds (<c>timeout</c> query parameter). Default 1.</summary>
    public int TimeoutSeconds { get; set; } = 1;

    /// <summary>Maximum envelopes per poll (<c>max_messages</c>); <see langword="null"/> for the API default.</summary>
    public int? MaxMessages { get; set; }

    /// <summary>Do not download attachments into the container (<c>ignore_attachments</c>).</summary>
    public bool IgnoreAttachments { get; set; }

    /// <summary>Skip story messages (<c>ignore_stories</c>). Default <see langword="true"/>.</summary>
    public bool IgnoreStories { get; set; } = true;

    /// <summary>Let the container send read receipts for received messages (<c>send_read_receipts</c>).</summary>
    public bool SendReadReceipts { get; set; }

    /// <summary>Upper bound of the exponential backoff after failed polls. Default 30 s.</summary>
    public TimeSpan MaxErrorBackoff { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>WebSocket receiving (<c>json-rpc</c> / <c>json-rpc-native</c> modes).</summary>
public sealed class WebSocketOptions
{
    /// <summary>Initial reconnect delay after the connection dropped or failed. Default 1 s.</summary>
    public TimeSpan ReconnectMinDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum reconnect delay (the delay doubles per failed attempt up to this value). Default 30 s.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>WebSocket keep-alive (ping) interval; keeps proxies from closing idle connections. Default 20 s.</summary>
    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Receive buffer size in bytes; larger frames are assembled from several reads. Default 16 KiB.</summary>
    public int ReceiveBufferSize { get; set; } = 16 * 1024;
}

/// <summary>HTTP client resilience settings.</summary>
public sealed class HttpOptions
{
    /// <summary>Timeout of a single HTTP attempt. Default 30 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retries for idempotent requests (GET/PUT/DELETE). Sends (POST) are never retried to avoid duplicate
    /// messages. 0 disables retries. Default 3.
    /// </summary>
    public int RetryCount { get; set; } = 3;
}

/// <summary>Command system settings.</summary>
public sealed class CommandOptions
{
    /// <summary>The prefix used when <see cref="Prefixes"/> is empty.</summary>
    public const string DefaultPrefix = "/";

    /// <summary>Command prefixes, e.g. <c>["/", "!"]</c>. The longest matching prefix wins. Defaults to <c>/</c>.</summary>
    public List<string> Prefixes { get; set; } = [];

    /// <summary>Whether command names and aliases are matched case-sensitively. Default <see langword="false"/>.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Reply when a prefixed message does not match any command. Default <see langword="true"/>.</summary>
    public bool RespondToUnknown { get; set; } = true;

    /// <summary>Reply for unknown commands. Format placeholders: <c>{0}</c> = command name, <c>{1}</c> = used prefix.</summary>
    public string UnknownCommandMessage { get; set; } = "Unknown command '{0}'. Send {1}help for a list of commands.";

    /// <summary>
    /// Append "Did you mean /help?" to unknown-command replies when a visible command name is one or two typos away.
    /// Default <see langword="true"/>.
    /// </summary>
    public bool SuggestSimilarCommands { get; set; } = true;

    /// <summary>Reply when a command throws an exception (details are only logged).</summary>
    public string ErrorMessage { get; set; } = "Sorry, something went wrong while executing this command.";

    /// <summary>Quote the triggering message in command replies. Default <see langword="false"/>.</summary>
    public bool QuoteReplies { get; set; }

    /// <summary>Register the built-in <c>help</c> command. Default <see langword="true"/>.</summary>
    public bool EnableHelp { get; set; } = true;

    /// <summary>
    /// Commands per <c>/help</c> page; <c>/help 2</c> shows the second page. 0 disables paging. Default 20.
    /// </summary>
    public int HelpPageSize { get; set; } = 20;

    /// <summary>Phone numbers or UUIDs allowed to run <c>[RequireAdmin]</c> commands.</summary>
    public List<string> Admins { get; set; } = [];

    /// <summary><see cref="Prefixes"/>, or <see cref="DefaultPrefix"/> when none are configured.</summary>
    public IReadOnlyList<string> EffectivePrefixes => Prefixes.Count == 0 ? [DefaultPrefix] : Prefixes;
}

/// <summary>Filters which senders are processed at all (applied before events and commands).</summary>
public sealed class AccessControlOptions
{
    /// <summary>If not empty, only these senders (phone numbers or UUIDs) are processed.</summary>
    public List<string> AllowedSenders { get; set; } = [];

    /// <summary>Senders (phone numbers or UUIDs) whose messages are ignored.</summary>
    public List<string> BlockedSenders { get; set; } = [];

    /// <summary>Ignore messages sent by the receiving account itself (e.g. from its other devices). Default <see langword="true"/>.</summary>
    public bool IgnoreOwnMessages { get; set; } = true;
}

/// <summary>Fixed-window rate limiting per sender, protecting against spam and command flooding.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Messages allowed per sender per <see cref="Window"/>. 0 (default) disables rate limiting.</summary>
    public int PermitsPerWindow { get; set; }

    /// <summary>Length of the rate limit window. Default 1 minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
