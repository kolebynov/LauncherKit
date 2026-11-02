namespace LauncherKit.Core.FileSources;

/// <summary>
/// Shapes reads of an underlying stream to the rate the limiter allows. Truncating the read to the
/// granted amount leaves the remaining bytes in the TCP receive window, so the sender slows down
/// instead of the launcher buffering data it is not allowed to consume yet.
/// </summary>
internal sealed class ThrottledFileStream : IFileStream
{
	private readonly IFileStream _innerStream;
	private readonly IDownloadRateLimiter _rateLimiter;

	public long Position => _innerStream.Position;

	public ThrottledFileStream(IFileStream innerStream, IDownloadRateLimiter rateLimiter)
	{
		_innerStream = innerStream;
		_rateLimiter = rateLimiter;
	}

	public async ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (buffer.IsEmpty)
		{
			return await _innerStream.Read(buffer, cancellationToken);
		}

		var granted = await _rateLimiter.AcquireAtMost(buffer.Length, cancellationToken);
		var read = 0;
		try
		{
			read = await _innerStream.Read(buffer[..granted], cancellationToken);
			return read;
		}
		finally
		{
			_rateLimiter.Refund(granted - read);
		}
	}

	public ValueTask DisposeAsync() => _innerStream.DisposeAsync();
}
