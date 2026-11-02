using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Core.Update;

public interface IUpdateProvider
{
	ValueTask<ModpackUpdateInfo?> GetModpackUpdateInfo(CancellationToken cancellationToken);

	/// <summary>
	/// The main STB versions the feed offers to this user, newest first.
	/// </summary>
	ValueTask<IReadOnlyList<AvailableModpackVersion>> GetAvailableModpackVersions(CancellationToken cancellationToken);

	/// <summary>
	/// A full (re)installation of <paramref name="modpackFeedItem"/> with all its patches.
	/// </summary>
	ValueTask<ModpackUpdateInfo> GetModpackInstallInfo(ModpackFeedItem modpackFeedItem, CancellationToken cancellationToken);

	ValueTask<LauncherUpdateInfo?> GetLauncherUpdateInfo(CancellationToken cancellationToken);
}
