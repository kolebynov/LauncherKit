﻿namespace LauncherKit.Core.FileSources;

internal sealed class FileSystemFileStream : IFileStream
{
	private readonly FileStream _fileStream;
	private readonly long _size;
	private long _position;

	public long Position => _position;

	public FileSystemFileStream(FileStream fileStream, long? fromInclusive, long? toInclusive)
	{
		_fileStream = fileStream;
		_fileStream.Position = fromInclusive.GetValueOrDefault();
		_size = (toInclusive ?? fileStream.Length - 1) + 1 - _fileStream.Position;
	}

	public async ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (_position >= _size)
		{
			return 0;
		}

		var read = await _fileStream.ReadAsync(buffer, cancellationToken);
		read = (int)Math.Min(read, _size - _position);
		_position += read;

		return read;
	}

	public ValueTask DisposeAsync() => _fileStream.DisposeAsync();
}