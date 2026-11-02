using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Update;

public sealed class UpdateProvider : IUpdateProvider
{
	private readonly IFeedProvider _feedProvider;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IAppTaskProvider _appTaskProvider;
	private readonly IAppVersionProvider _appVersionProvider;

	public UpdateProvider(IFeedProvider feedProvider, IModpackVersionProvider modpackVersionProvider, IAppTaskProvider appTaskProvider,
		IAppVersionProvider appVersionProvider)
	{
		_feedProvider = feedProvider;
		_modpackVersionProvider = modpackVersionProvider;
		_appTaskProvider = appTaskProvider;
		_appVersionProvider = appVersionProvider;
	}

	public async ValueTask<ModpackUpdateInfo?> GetModpackUpdateInfo(CancellationToken cancellationToken)
	{
		var installedModpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		var modpackFeedItems = await _feedProvider.GetModpackFeedItems(cancellationToken);
		var newestModpack = modpackFeedItems.MaxBy(x => x.Version);
		if (newestModpack == null)
		{
			return null;
		}

		var hasMajorUpdate = newestModpack.InitialVersion > (installedModpackVersion ?? new NuGetVersion(0, 0, 0));
		var versionToInstall = hasMajorUpdate ? newestModpack.Version : installedModpackVersion!;
		var patchesToInstall = newestModpack.Patches?.Where(x => x.Version > versionToInstall).OrderBy(x => x.Version).ToArray() ?? [];
		var updateAppTasks = new List<AppTask>();
		if (hasMajorUpdate)
		{
			updateAppTasks.Add(await _appTaskProvider.CreateInstallModpackTask(newestModpack, patchesToInstall, cancellationToken));
		}
		else
		{
			foreach (var patchToInstall in patchesToInstall)
			{
				updateAppTasks.Add(await _appTaskProvider.CreateInstallPatchTask(patchToInstall, false, cancellationToken));
			}
		}

		if (updateAppTasks.Count == 0)
		{
			return null;
		}

		return new ModpackUpdateInfo
		{
			UpdateAppTask = AggregateAppTasks(updateAppTasks),
			UpdateType = (hasMajorUpdate, installedModpackVersion == null) switch
			{
				(_, true) => UpdateType.InitialInstall.Instance,
				(true, _) => UpdateType.MajorUpdate.Instance,
				(false, _) => new UpdateType.PatchUpdate(patchesToInstall.Any(x => x.RequireNewGame)),
			},
			Version = patchesToInstall.LastOrDefault()?.Version ?? versionToInstall,
		};
	}

	public async ValueTask<IReadOnlyList<AvailableModpackVersion>> GetAvailableModpackVersions(CancellationToken cancellationToken)
	{
		var installedModpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		return (await _feedProvider.GetModpackFeedItems(cancellationToken))
			.Select(x =>
			{
				var latestVersion = GetPatchesToInstall(x).LastOrDefault()?.Version ?? x.Version;
				var isInstalled = installedModpackVersion != null && installedModpackVersion >= x.InitialVersion &&
				                  installedModpackVersion <= latestVersion;
				return new AvailableModpackVersion(x, latestVersion, isInstalled);
			})
			.OrderByDescending(x => x.LatestVersion)
			.ToArray();
	}

	public async ValueTask<ModpackUpdateInfo> GetModpackInstallInfo(ModpackFeedItem modpackFeedItem, CancellationToken cancellationToken)
	{
		var patchesToInstall = GetPatchesToInstall(modpackFeedItem);
		var updateAppTask = await _appTaskProvider.CreateInstallModpackTask(modpackFeedItem, patchesToInstall, cancellationToken);

		return new ModpackUpdateInfo
		{
			UpdateAppTask = updateAppTask,
			UpdateType = _modpackVersionProvider.InstalledModpackVersion == null
				? UpdateType.InitialInstall.Instance
				: UpdateType.MajorUpdate.Instance,
			Version = patchesToInstall.LastOrDefault()?.Version ?? modpackFeedItem.Version,
		};
	}

	/// <summary>
	/// The patches a full installation of <paramref name="modpackFeedItem"/> applies, in version order.
	/// </summary>
	private static ModpackPatchFeedItem[] GetPatchesToInstall(ModpackFeedItem modpackFeedItem) =>
		modpackFeedItem.Patches?.Where(x => x.Version > modpackFeedItem.Version).OrderBy(x => x.Version).ToArray() ?? [];

	public async ValueTask<LauncherUpdateInfo?> GetLauncherUpdateInfo(CancellationToken cancellationToken)
	{
		var launcherFeedItems = await _feedProvider.GetLauncherFeedItems(cancellationToken);
		var currentVersion = _appVersionProvider.Version;
		LauncherFeedItem? newestLauncher = null;
		var supportsNewModpackVersions = false;
		foreach (var launcherFeedItem in launcherFeedItems)
		{
			if (launcherFeedItem.Version <= currentVersion)
			{
				continue;
			}

			supportsNewModpackVersions |= launcherFeedItem.SupportsNewModpackVersions;
			if (newestLauncher == null || launcherFeedItem.Version > newestLauncher.Version)
			{
				newestLauncher = launcherFeedItem;
			}
		}

		return newestLauncher == null
			? null
			: new LauncherUpdateInfo { NewestLauncher = newestLauncher, SupportsNewModpackVersions = supportsNewModpackVersions };
	}

	private static AppTask AggregateAppTasks(IReadOnlyList<AppTask> appTasks)
	{
		var lastAppTask = appTasks[^1];
		return appTasks.Count switch
		{
			1 => lastAppTask,
			_ => new AppTask(
				lastAppTask.Id,
				lastAppTask.Description,
				lastAppTask.MicroTasksProvider,
				lastAppTask.Dependencies.Append(AggregateAppTasks(appTasks.Take(appTasks.Count - 1).ToArray())).ToArray(),
				lastAppTask.CompleteVerifier)
			{
				OnComplete = lastAppTask.OnComplete,
			},
		};
	}
}