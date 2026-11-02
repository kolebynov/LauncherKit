﻿namespace LauncherKit.Core.FileSources;

public interface IFileStream : IAsyncDisposable
{
	long Position { get; }

	ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken);
}