﻿using LauncherKit.Core.Models;

namespace LauncherKit.Core.Feed;

public sealed class DownloadInfo
{
	public static readonly DownloadInfo Empty = new() { S3Path = "empty" };

	public required string S3Path { get; init; }

	public Sha256Hash? Hash { get; init; }

	public string FileName => Path.GetFileName(S3Path);
}