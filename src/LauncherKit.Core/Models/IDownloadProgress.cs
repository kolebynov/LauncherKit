﻿namespace LauncherKit.Core.Models;

public interface IDownloadProgress : IOperationProgress
{
	public long DownloadedBytes { get; }

	public long TotalBytes { get; }

	public double Speed { get; }

	public bool IsPaused { get; }

	public ValueTask Pause();

	public ValueTask Resume();
}