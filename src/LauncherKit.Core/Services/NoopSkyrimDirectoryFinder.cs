using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

internal sealed class NoopSkyrimDirectoryFinder : ISkyrimDirectoryFinder
{
	public string? FindSkyrimDirectory() => null;
}