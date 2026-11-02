﻿namespace LauncherKit.Core.Utils;

public union SkyrimDirectoryCheckResult(SkyrimDirectoryCheckResult.NotSkyrimDirectory, SkyrimDirectoryCheckResult.SkyrimDirectory)
{
	public sealed record NotSkyrimDirectory;

	public sealed record SkyrimDirectory(Version SkyrimVersion);
}

public interface ISkyrimInstallationChecker
{
	SkyrimDirectoryCheckResult CheckSkyrimDirectory(string? directory);
}
