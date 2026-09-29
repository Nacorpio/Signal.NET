namespace Signal.Infrastructure.Http;

/// <summary>
/// A read-only stream over an HTTP response body that also owns the <see cref="HttpResponseMessage"/>:
/// disposing the stream disposes the response and returns the connection to the pool.
/// </summary>
/// <param name="inner">The response body stream.</param>
/// <param name="response">The response to dispose together with the stream.</param>
internal sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
{
    private bool _disposed;

    public override bool CanRead => !_disposed && inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
        inner.CopyToAsync(destination, bufferSize, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            inner.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await inner.DisposeAsync();
            response.Dispose();
        }

        await base.DisposeAsync();
    }
}
