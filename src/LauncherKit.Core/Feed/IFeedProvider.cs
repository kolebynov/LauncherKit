using R3;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Core.Feed;

public interface IFeedProvider
{
	ValueTask<IReadOnlyList<InstallComponent>> GetInstallComponents(CancellationToken cancellationToken);

	ValueTask<IReadOnlyList<Prerequisite>> GetPrerequisites(CancellationToken cancellationToken);

	/// <summary>
	/// Raised when the beta versions <see cref="GetModpackFeedItems"/> returns may have changed: a beta code was activated,
	/// or the linked Discord account changed.
	/// </summary>
	Observable<Unit> BetaAccessChanged { get; }

	/// <summary>
	/// Downloads of the feed from the server, raised on the loading thread. Each <see cref="FeedLoadEvent.Started"/>
	/// is followed by <see cref="FeedLoadEvent.Succeeded"/> or <see cref="FeedLoadEvent.Failed"/>, unless the
	/// provider is disposed meanwhile. Reads served from the cache raise nothing.
	/// </summary>
	Observable<FeedLoadEvent> LoadEvents { get; }

	ValueTask<ActivateBetaCodeResult> ActivateBetaCode(string betaCode, CancellationToken cancellationToken);

	/// <summary>
	/// Modpack versions offered to the user. Releases are always included; a prerelease only when an activated beta code's
	/// <see cref="FeedData.BetaVersions"/> range covers it, when the linked Discord account has a role of
	/// <see cref="FeedData.DeveloperRoleIds"/>, or when it is <see cref="ModpackFeedItem.IsAvailableForSponsors"/> and the
	/// account has a role of <see cref="FeedData.SponsorRoleIds"/> (read through the account cache; a failed read
	/// hides it). A prerelease no beta code covers is therefore not offered to other users either.
	/// </summary>
	ValueTask<IEnumerable<ModpackFeedItem>> GetModpackFeedItems(CancellationToken cancellationToken);

	ValueTask<BetaAccessRoles> GetBetaAccessRoles(CancellationToken cancellationToken);

	ValueTask<IReadOnlyList<LauncherFeedItem>> GetLauncherFeedItems(CancellationToken cancellationToken);

	/// <summary>Every mod of the feed, including discontinued ones, in display order within each category.</summary>
	ValueTask<IReadOnlyList<ModFeedData>> GetModFeedDatas(CancellationToken cancellationToken);

	/// <summary>Sections of the mod cards, in display order.</summary>
	ValueTask<IReadOnlyList<ModCategoryFeedData>> GetModCategories(CancellationToken cancellationToken);

	ValueTask<IReadOnlyList<ModpackChangelogFeedItem>> GetModpackChangelogs(CancellationToken cancellationToken);

	ValueTask<DownloadInfo> GetSevenZipDownloadInfo(CancellationToken cancellationToken);

	ValueTask<DownloadInfo> GetSkyrimDownloadInfo(CancellationToken cancellationToken);
}
