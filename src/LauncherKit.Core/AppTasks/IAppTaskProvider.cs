using NuGet.Versioning;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.AppTasks;

public interface IAppTaskProvider
{
	ValueTask<AppTask> CreateRunGameTask(StartGameVariant startGameVariant, CancellationToken cancellationToken);

	AppTask CreateUpdateLauncherTask(DownloadInfo downloadInfo, NuGetVersion version);

	/// <summary>
	/// Reinstalls STB from <paramref name="modpackFeedItem"/>, then installs <paramref name="patches"/> in order and the
	/// optional components the user chooses when the installation starts.
	/// </summary>
	ValueTask<AppTask> CreateInstallModpackTask(ModpackFeedItem modpackFeedItem, IReadOnlyList<ModpackPatchFeedItem> patches,
		CancellationToken cancellationToken);

	/// <summary>
	/// Removes STB by deleting the Mod Organizer directory with its mods, profiles and saves; Skyrim stays.
	/// </summary>
	AppTask CreateUninstallModpackTask();

	/// <summary>
	/// Deletes the saves of the selected Mod Organizer profile except the <paramref name="savesToKeep"/> newest ones,
	/// then passes the result to <paramref name="onDeleted"/> on the task queue's thread. A failure or cancellation
	/// while deleting does not fail the task: it is passed in <see cref="DeletedSavesInfo.Error"/> with the saves
	/// deleted before it.
	/// </summary>
	AppTask CreateDeleteOldSavesTask(int savesToKeep, Action<DeletedSavesInfo> onDeleted);

	ValueTask<AppTask> CreateInstallPatchTask(
		ModpackPatchFeedItem modpackPatchFeedItem, bool forceUpdate, CancellationToken cancellationToken);
}
