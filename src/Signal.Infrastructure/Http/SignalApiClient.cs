using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Signal.Application.Abstractions;
using Signal.Infrastructure.Http.Dtos;

namespace Signal.Infrastructure.Http;

/// <summary>
/// Thin typed HTTP client for signal-cli-rest-api, created by <c>IHttpClientFactory</c>. It only handles
/// (de)serialization and error translation; timeouts, retries and circuit breaking are configured on the handler
/// pipeline in <c>AddSignalInfrastructure</c>. Paths are relative (e.g. <c>v2/send</c>) to the configured base URL.
/// </summary>
/// <param name="http">The configured HTTP client (base address = <c>Signal:BaseUrl</c>).</param>
internal sealed class SignalApiClient(HttpClient http)
{
    /// <summary>The configured base address.</summary>
    public Uri? BaseAddress => http.BaseAddress;

    /// <summary>Escapes a path segment (e.g. <c>+49…</c> becomes <c>%2B49…</c>, <c>/</c> in group ids becomes <c>%2F</c>).</summary>
    /// <param name="segment">The raw segment.</param>
    /// <returns>The escaped segment.</returns>
    public static string Escape(string segment) => Uri.EscapeDataString(segment);

    /// <summary>Sends a GET request and deserializes the JSON response.</summary>
    /// <exception cref="SignalApiException">Non-success status code or empty body.</exception>
    public async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken)
            ?? throw new SignalApiException(response.StatusCode, "Empty response body.", path);
    }

    /// <summary>Sends a GET request and returns the raw body (attachments, QR codes).</summary>
    /// <exception cref="SignalApiException">Non-success status code.</exception>
    public async Task<byte[]> GetBytesAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a GET request and returns the body as an unbuffered stream (headers are read, the body is not).
    /// The returned download owns the HTTP response; disposing it releases the connection.
    /// </summary>
    /// <exception cref="SignalApiException">Non-success status code.</exception>
    public async Task<AttachmentDownload> GetStreamAsync(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, path, cancellationToken);
            var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new AttachmentDownload(
                new ResponseStream(body, response),
                response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentLength);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Sends a GET request and reports whether it succeeded (health checks); never throws for status codes.</summary>
    public async Task<bool> IsSuccessAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>Sends a request without body and without expected response body.</summary>
    /// <exception cref="SignalApiException">Non-success status code.</exception>
    public async Task SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, path, cancellationToken);
    }

    /// <summary>Sends a JSON body and ignores the response body.</summary>
    /// <exception cref="SignalApiException">Non-success status code.</exception>
    public async Task SendAsync<TBody>(HttpMethod method, string path, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, bodyType, cancellationToken);
    }

    /// <summary>Sends a JSON body and deserializes the JSON response.</summary>
    /// <exception cref="SignalApiException">Non-success status code or empty body.</exception>
    public async Task<TResult> SendAsync<TBody, TResult>(
        HttpMethod method, string path, TBody body, JsonTypeInfo<TBody> bodyType, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, bodyType, cancellationToken);
        return await response.Content.ReadFromJsonAsync(resultType, cancellationToken)
            ?? throw new SignalApiException(response.StatusCode, "Empty response body.", path);
    }

    /// <summary>
    /// Sends a JSON body and deserializes the response if it has one. For endpoints that answer either with a
    /// body (e.g. 201) or without one (204).
    /// </summary>
    /// <returns>The deserialized body, or <see langword="default"/> for an empty response.</returns>
    /// <exception cref="SignalApiException">Non-success status code.</exception>
    public async Task<TResult?> SendForOptionalResultAsync<TBody, TResult>(
        HttpMethod method, string path, TBody body, JsonTypeInfo<TBody> bodyType, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
        where TResult : class
    {
        using var response = await SendCoreAsync(method, path, body, bodyType, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? null : JsonSerializer.Deserialize(content, resultType);
    }

    /// <summary>Sends a JSON body; returns the (successful) response, which the caller must dispose.</summary>
    private async Task<HttpResponseMessage> SendCoreAsync<TBody>(
        HttpMethod method, string path, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, bodyType) };
        var response = await http.SendAsync(request, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, path, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Translates non-success responses into <see cref="SignalApiException"/> using the API's <c>error</c> field.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        string? error;
        try
        {
            error = JsonSerializer.Deserialize(content, SignalRestJsonContext.Default.ErrorResponseDto)?.Error ?? content;
        }
        catch (JsonException)
        {
            error = content;
        }

        throw new SignalApiException(response.StatusCode, string.IsNullOrWhiteSpace(error) ? null : error, path.Split('?')[0]);
    }
}
