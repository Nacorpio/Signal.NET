using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;

namespace Signal.Infrastructure.Tests;

public class AttachmentStreamingTests
{
    /// <summary>Content that records when it is disposed (i.e. when the response is released).</summary>
    private sealed class TrackingContent(byte[] body, string mediaType) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(body, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public TrackingContent Init()
        {
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return this;
        }
    }

    [Fact]
    public async Task OpenReadAsync_streams_body_with_metadata_and_releases_response_on_dispose()
    {
        var body = Enumerable.Range(0, 256 * 1024).Select(i => (byte)i).ToArray();
        var content = new TrackingContent(body, "image/png").Init();
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        await using var provider = TestServices.Build(handler);
        var attachments = provider.GetRequiredService<IAttachmentService>();

        var download = await attachments.OpenReadAsync("abc/1.png");

        // Still open after OpenReadAsync returned: the body is read from the live response, not a buffer.
        Assert.False(content.Disposed);
        Assert.Equal("image/png", download.ContentType);
        Assert.Equal(body.Length, download.Length);
        Assert.False(download.Content.CanSeek);

        using var copy = new MemoryStream();
        await download.Content.CopyToAsync(copy);
        Assert.Equal(body, copy.ToArray());

        await download.DisposeAsync();
        Assert.True(content.Disposed);
        Assert.Equal("/v1/attachments/abc%2F1.png", Assert.Single(handler.Requests).PathAndQuery);
    }

    [Fact]
    public async Task OpenReadAsync_throws_for_missing_attachment()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""{"error":"attachment not found"}""", HttpStatusCode.NotFound));
        await using var provider = TestServices.Build(handler, o => o.Http.RetryCount = 0);

        var error = await Assert.ThrowsAsync<SignalApiException>(() => provider.GetRequiredService<IAttachmentService>().OpenReadAsync("missing"));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("attachment not found", error.ApiError);
    }

    /// <summary>A custom implementation written before OpenReadAsync existed.</summary>
    private sealed class LegacyAttachmentService : IAttachmentService
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<byte[]> DownloadAsync(string attachmentId, CancellationToken cancellationToken = default) => Task.FromResult<byte[]>([1, 2, 3]);

        public Task DeleteAsync(string attachmentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Existing_implementations_get_a_buffered_OpenReadAsync_by_default()
    {
        IAttachmentService legacy = new LegacyAttachmentService();

        await using var download = await legacy.OpenReadAsync("x");
        using var copy = new MemoryStream();
        await download.Content.CopyToAsync(copy);

        Assert.Equal([1, 2, 3], copy.ToArray());
        Assert.Null(download.ContentType);
        Assert.Null(download.Length);
    }
}
