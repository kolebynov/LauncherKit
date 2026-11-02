﻿namespace LauncherKit.Core.Settings;

public sealed class UrlsSettings
{
	public required IReadOnlyDictionary<string, UrlSettings> Urls { get; set; }
}