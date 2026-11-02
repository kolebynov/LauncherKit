﻿using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;

namespace LauncherKit.Core.Services.Abstractions;

public interface IFileSourceDownloader
{
	Task Download(IFileSource source, OpenedFile destination, Func<DownloadFileProgress, ValueTask>? progressTracker,
		CancellationToken cancellationToken);
}

public readonly record struct DownloadFileProgress(long DownloadedBytes, long TotalBytes, double Speed);