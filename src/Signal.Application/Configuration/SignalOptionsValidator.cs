using Microsoft.Extensions.Options;
using Signal.Domain;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Configuration;

/// <summary>
/// Validates <see cref="SignalOptions"/>. Registered automatically; combined with <c>ValidateOnStart()</c>
/// an invalid configuration stops the host at startup with a list of all problems.
/// </summary>
public sealed class SignalOptionsValidator : IValidateOptions<SignalOptions>
{
    /// <summary>Checks every setting and collects all errors.</summary>
    /// <param name="name">The options name (unused; Signal.NET uses the default options instance).</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>Success, or a failure listing every invalid setting.</returns>
    public ValidateOptionsResult Validate(string? name, SignalOptions options)
    {
        var errors = new List<string>();

        if (options.BaseUrl is not { IsAbsoluteUri: true } url || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Signal:BaseUrl must be an absolute http(s) URL.");
        }

        if (!Enum.IsDefined(options.Mode))
        {
            errors.Add($"Signal:Mode '{options.Mode}' is invalid. Use Normal, Native, JsonRpc or JsonRpcNative.");
        }

        if (options.Accounts.Count == 0)
        {
            errors.Add("Signal:Accounts must contain at least one registered phone number.");
        }

        errors.AddRange(options.Accounts
            .Where(a => !PhoneNumber.TryParse(a, out _))
            .Select(a => $"Signal:Accounts entry '{a}' is not a valid E.164 phone number."));

        if (options.MaxConcurrency < 1)
        {
            errors.Add("Signal:MaxConcurrency must be at least 1.");
        }

        var receive = options.Receive;
        if (receive.PollingInterval <= TimeSpan.Zero)
        {
            errors.Add("Signal:Receive:PollingInterval must be positive.");
        }

        if (receive.TimeoutSeconds < 0)
        {
            errors.Add("Signal:Receive:TimeoutSeconds must not be negative.");
        }

        if (receive.MaxMessages is <= 0)
        {
            errors.Add("Signal:Receive:MaxMessages must be positive when set.");
        }

        var ws = options.WebSocket;
        if (ws.ReconnectMinDelay <= TimeSpan.Zero || ws.ReconnectMaxDelay < ws.ReconnectMinDelay)
        {
            errors.Add("Signal:WebSocket:ReconnectMinDelay must be positive and not exceed ReconnectMaxDelay.");
        }

        if (ws.ReceiveBufferSize < 1024)
        {
            errors.Add("Signal:WebSocket:ReceiveBufferSize must be at least 1024 bytes.");
        }

        if (options.Http.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Signal:Http:Timeout must be positive.");
        }
        else if (!options.Mode.IsStreaming && options.Http.Timeout <= TimeSpan.FromSeconds(receive.TimeoutSeconds + 5))
        {
            // A long poll must be allowed to finish before the HTTP attempt times out.
            errors.Add("Signal:Http:Timeout must exceed Signal:Receive:TimeoutSeconds by at least 5 seconds (long polling).");
        }

        if (options.Http.RetryCount < 0)
        {
            errors.Add("Signal:Http:RetryCount must not be negative.");
        }

        if (options.Commands.Prefixes.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Signal:Commands:Prefixes must not contain empty values.");
        }

        if (options.Commands.HelpPageSize < 0)
        {
            errors.Add("Signal:Commands:HelpPageSize must not be negative (0 disables paging).");
        }

        if (options.RateLimit.PermitsPerWindow < 0 || options.RateLimit.Window <= TimeSpan.Zero)
        {
            errors.Add("Signal:RateLimit:PermitsPerWindow must not be negative and Window must be positive.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
