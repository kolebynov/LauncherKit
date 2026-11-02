﻿namespace LauncherKit.Core.FileSources;

internal sealed class HttpFileStream : IFileStream
{
	private readonly Stream _httpStream;
	private long _position;

	public long Position => _position;

	public HttpFileStream(Stream httpStream)
	{
		_httpStream = httpStream;
		_position = 0;
	}

	public async ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		var read = await _httpStream.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
		Interlocked.Add(ref _position, read);
		return read;
	}

	public ValueTask DisposeAsync() => _httpStream.DisposeAsync();
}