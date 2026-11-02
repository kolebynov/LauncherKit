﻿using LauncherKit.Core.Feed;

namespace LauncherKit.Core.AppTasks.MicroTasks;

/// <summary>Where a file is downloaded from: a <see cref="DownloadInfo"/> in the S3 bucket, or a plain HTTP URL.</summary>
public union DownloadFileSource(DownloadInfo, DownloadFileSource.Http)
{
	public sealed record Http(Uri Uri, string FileName);
}