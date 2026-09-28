using System.Net;

namespace Signal.Application.Abstractions;

/// <summary>The signal-cli-rest-api answered with a non-success status code.</summary>
/// <remarks>
/// Connection failures surface as <see cref="HttpRequestException"/> and timeouts as <see cref="TimeoutException"/>
/// or <see cref="OperationCanceledException"/>; this exception means the API was reached but refused the request.
/// </remarks>
/// <param name="statusCode">The HTTP status code.</param>
/// <param name="apiError">The <c>error</c> field of the response body, or the raw body.</param>
/// <param name="path">The request path (without query string).</param>
public sealed class SignalApiException(HttpStatusCode statusCode, string? apiError, string? path = null)
    : Exception($"Signal API request{(path is null ? "" : $" '{path}'")} failed with {(int)statusCode} ({statusCode}): {apiError ?? "no details"}")
{
    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The <c>error</c> field of the API response (e.g. <c>Unregistered user</c>), if any.</summary>
    public string? ApiError { get; } = apiError;
}
