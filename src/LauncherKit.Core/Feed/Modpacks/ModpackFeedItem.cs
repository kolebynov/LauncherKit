using NuGet.Versioning;

namespace LauncherKit.Core.Feed.Modpacks;

public sealed class ModpackFeedItem
{
	public required NuGetVersion InitialVersion { get; init; }

	public required NuGetVersion Version { get; init; }

	public required DownloadInfo DownloadInfo { get; init; }

	public IReadOnlyList<ModpackPatchFeedItem>? Patches { get; init; }

	/// <summary>
	/// A prerelease that sponsors (<see cref="FeedData.SponsorRoleIds"/>) get without a beta code.
	/// Optional, <see langword="false"/> when missing.
	/// </summary>
	public bool IsAvailableForSponsors { get; init; }
}