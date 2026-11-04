using NuGet.Versioning;

namespace LauncherKit.Core.Feed;

public sealed class LauncherFeedItem
{
	public required NuGetVersion Version { get; init; }

	public required DownloadInfo DownloadInfo { get; init; }

	public LocalizedText? ChangelogPath { get; init; }

	/// <summary>
	/// This launcher version is the first one able to install or update to new modpack versions,
	/// so users of older launchers must update to get them.
	/// </summary>
	public bool SupportsNewModpackVersions { get; init; }
}