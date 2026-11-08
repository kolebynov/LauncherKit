﻿namespace LauncherKit.Core;

public static class AppConstants
{
	/// <summary>Feed server client for small documents (the feed, Markdown): the response body is read under its timeouts.</summary>
	public const string S3HttpClient = "S3Client";
	/// <summary>Feed server client for file downloads: the body is streamed, so callers detect stalled transfers themselves.</summary>
	public const string S3DownloadHttpClient = "S3DownloadClient";
	public const string AnalyticsHttpClient = "AnalyticsClient";
	public const string DiscordHttpClient = "DiscordClient";
	public const string HttpFileSourceHttpClient = "HttpFileSourceClient";
}