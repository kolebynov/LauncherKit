using LauncherKit.Core.Feed;

namespace LauncherKit.Core.Update;

public sealed class LauncherUpdateInfo
{
	/// <summary>The newest launcher version, which the update installs.</summary>
	public required LauncherFeedItem NewestLauncher { get; init; }

	/// <summary>
	/// Some launcher version newer than the running one, up to and including the newest, has
	/// <see cref="LauncherFeedItem.SupportsNewModpackVersions"/> set, so updating is needed to get new STB versions.
	/// </summary>
	public required bool SupportsNewModpackVersions { get; init; }
}
