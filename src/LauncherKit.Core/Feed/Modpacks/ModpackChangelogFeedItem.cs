using NuGet.Versioning;

namespace LauncherKit.Core.Feed.Modpacks;

public sealed class ModpackChangelogFeedItem
{
	public required NuGetVersion Version { get; init; }

	public required LocalizedText Path { get; init; }
}