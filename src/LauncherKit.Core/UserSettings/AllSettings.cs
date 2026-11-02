﻿using LauncherKit.Infrastructure;

namespace LauncherKit.Core.UserSettings;

public sealed class AllSettings
{
	public required AppSettings App { get; init; }

	public required DownloadSettings Download { get; init; }

	public required WindowSettings Window { get; init; }
}