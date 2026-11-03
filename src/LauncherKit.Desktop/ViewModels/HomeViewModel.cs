using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using NuGet.Versioning;
using R3;
using LauncherKit.Core;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Update;

namespace LauncherKit.Desktop.ViewModels;

public enum ModpackInstallUnavailableReason { NoAvailableVersions }

public partial class HomeViewModel : BaseActivatableViewModel, IDisposable
{
	private readonly IAppTaskProvider _appTaskProvider;
	private readonly IAppTaskQueue _appTaskQueue;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IFeedProvider _feedProvider;
	private readonly IAppVersionProvider _appVersionProvider;
	private readonly IGlobalKeyValueStorage _globalKeyValueStorage;
	private readonly IUpdateProvider _updateProvider;
	private readonly IUiDispatcher _dispatcher;
	private readonly IDisposable _appTaskQueueSubscription;
	private readonly Subject<DeletedSavesInfo> _oldSavesDeleted = new();

	[ObservableProperty]
	public partial LauncherUpdateInfo? LauncherUpdateInfo { get; private set; }
	[ObservableProperty]
	public partial ModpackUpdateInfo? ModpackUpdateInfo { get; private set; }
	[ObservableProperty]
	public partial IReadOnlyList<AvailableModpackVersion> AvailableModpackVersions { get; private set; } = [];
	[ObservableProperty]
	public partial ModpackInstallUnavailableReason? ModpackInstallUnavailableReason { get; private set; }
	[ObservableProperty]
	public partial NuGetVersion? InstalledModpackVersion { get; private set; }
	[ObservableProperty]
	public partial IReadOnlyList<ModpackChangelogFeedItem> PendingModpackChangelog { get; private set; } = [];
	[ObservableProperty]
	public partial LauncherFeedItem? LauncherChangelogToShow { get; private set; }
	[ObservableProperty]
	public partial AppTaskViewModel? AppTaskViewModel { get; private set; }

	public Observable<DeletedSavesInfo> OldSavesDeleted => _oldSavesDeleted;
	public GraphPacksViewModel GraphPacksViewModel { get; }
	public OptionalModsViewModel OptionalModsViewModel { get; }

	public HomeViewModel(IAppTaskProvider appTaskProvider, IAppTaskQueue appTaskQueue,
		IModpackVersionProvider modpackVersionProvider, IUpdateProvider updateProvider, GraphPacksViewModel graphPacksViewModel,
		OptionalModsViewModel optionalModsViewModel, IFeedProvider feedProvider,
		IAppLogsCollector appLogsCollector,
		IAppVersionProvider appVersionProvider, IGlobalKeyValueStorage globalKeyValueStorage, IUiDispatcher dispatcher)
	{
		_appTaskProvider = appTaskProvider;
		_appTaskQueue = appTaskQueue;
		_modpackVersionProvider = modpackVersionProvider;
		_feedProvider = feedProvider;
		_appVersionProvider = appVersionProvider;
		_globalKeyValueStorage = globalKeyValueStorage;
		_updateProvider = updateProvider;
		_dispatcher = dispatcher;
		GraphPacksViewModel = graphPacksViewModel;
		OptionalModsViewModel = optionalModsViewModel;
		InstalledModpackVersion = modpackVersionProvider.InstalledModpackVersion;

		_appTaskQueueSubscription = appTaskQueue.Events.ObserveOnUi(dispatcher).Subscribe(evt =>
		{
			Action handle = evt switch
			{
				AppTaskQueue.Event.TaskQueued => static () => { },
				AppTaskQueue.Event.TaskStarted(var t) => () => AppTaskViewModel = new AppTaskViewModel(t, appLogsCollector, dispatcher),
				AppTaskQueue.Event.MicroTaskStarted(_, var mt, var c) => () => AppTaskViewModel?.SetRunningMicroTask(mt.Description, c),
				AppTaskQueue.Event.TaskFailed(_, var e) => () => AppTaskViewModel?.SetError(e),
				AppTaskQueue.Event.TaskCompleted => ClearActiveTask,
				AppTaskQueue.Event.TaskRemoved(var t, _) => () => { if (AppTaskViewModel?.AppTask == t) ClearActiveTask(); },
			};
			handle();
		});

		void ClearActiveTask()
		{
			AppTaskViewModel?.Complete();
			AppTaskViewModel = null;
		}

		this.WhenActivated(async (d, ct) =>
		{
			modpackVersionProvider.ObservePropertyChanged(x => x.InstalledModpackVersion)
				.ObserveOnUi(dispatcher)
				.SubscribeAwait(async (version, token) =>
				{
					var previous = InstalledModpackVersion;
					InstalledModpackVersion = version;
					await LoadPendingModpackChangelog(previous, version, token);
					await LoadUpdateInfo(token);
				}).AddTo(d);
			Observable.Interval(TimeSpan.FromMinutes(1), ct).Merge(feedProvider.BetaAccessChanged)
				.ObserveOnUi(dispatcher).SubscribeAwait((_, token) => LoadUpdateInfo(token)).AddTo(d);
			await LoadUpdateInfo(ct);
			await LoadLauncherChangelog(ct);
		});
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposing)
			return;
		_appTaskQueueSubscription.Dispose();
		_oldSavesDeleted.Dispose();
	}

	[RelayCommand]
	private void TryFailedTaskAgain() => _appTaskQueue.Resume();

	[RelayCommand]
	private Task UpdateModpack(CancellationToken cancellationToken) =>
		ModpackUpdateInfo != null ? UpdateModpack(ModpackUpdateInfo, cancellationToken) : Task.CompletedTask;

	[RelayCommand]
	private Task InstallModpackPatch(CancellationToken cancellationToken) =>
		ModpackUpdateInfo?.UpdateType is UpdateType.PatchUpdate ? UpdateModpack(ModpackUpdateInfo, cancellationToken) : Task.CompletedTask;

	[RelayCommand]
	private async Task InstallModpackVersion(AvailableModpackVersion version, CancellationToken cancellationToken) =>
		await UpdateModpack(await _updateProvider.GetModpackInstallInfo(version.FeedItem, cancellationToken), cancellationToken);

	[RelayCommand]
	private async Task StartGame(GameLaunch launch, CancellationToken cancellationToken) =>
		await _appTaskQueue.EnqueueAndWaitForCompletion(await _appTaskProvider.CreateRunGameTask(launch, cancellationToken), cancellationToken);

	[RelayCommand]
	private async Task UpdateLauncher(CancellationToken cancellationToken)
	{
		if (LauncherUpdateInfo != null)
			await _appTaskQueue.EnqueueAndWaitForCompletion(_appTaskProvider.CreateUpdateLauncherTask(
				LauncherUpdateInfo.NewestLauncher.DownloadInfo, LauncherUpdateInfo.NewestLauncher.Version), cancellationToken);
	}

	[RelayCommand]
	private async Task DeleteOldSaves(int keepCount, CancellationToken cancellationToken) =>
		await _appTaskQueue.EnqueueAndWaitForCompletion(_appTaskProvider.CreateDeleteOldSavesTask(keepCount,
			deleted => _dispatcher.Post(() => _oldSavesDeleted.OnNext(deleted))), cancellationToken);

	[RelayCommand]
	private async Task UninstallModpack(CancellationToken cancellationToken)
	{
		try
		{
			await _appTaskQueue.EnqueueAndWaitForCompletion(_appTaskProvider.CreateUninstallModpackTask(), cancellationToken);
		}
		finally
		{
			_modpackVersionProvider.RefreshInstalledModpackVersion();
			InstalledModpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		}
	}

	private async ValueTask LoadUpdateInfo(CancellationToken cancellationToken)
	{
		LauncherUpdateInfo = await _updateProvider.GetLauncherUpdateInfo(cancellationToken);
		ModpackUpdateInfo = await _updateProvider.GetModpackUpdateInfo(cancellationToken);
		AvailableModpackVersions = await _updateProvider.GetAvailableModpackVersions(cancellationToken);
		ModpackInstallUnavailableReason = ModpackUpdateInfo == null && InstalledModpackVersion == null
			? global::LauncherKit.Desktop.ViewModels.ModpackInstallUnavailableReason.NoAvailableVersions : null;
	}

	private async Task LoadLauncherChangelog(CancellationToken cancellationToken)
	{
		var launchers = await _feedProvider.GetLauncherFeedItems(cancellationToken);
		var latest = launchers.Where(x => x.Version <= _appVersionProvider.Version && x.ChangelogPath != null).MaxBy(x => x.Version);
		LauncherChangelogToShow = latest != null && !_globalKeyValueStorage.HasKey($"AppChangelogWasShown_{latest.Version}") ? latest : null;
	}

	public void MarkLauncherChangelogShown()
	{
		if (LauncherChangelogToShow is not { } changelog) return;
		_globalKeyValueStorage.Set($"AppChangelogWasShown_{changelog.Version}", "1");
		LauncherChangelogToShow = null;
	}

	public void MarkModpackChangelogShown() => PendingModpackChangelog = [];

	private async ValueTask LoadPendingModpackChangelog(NuGetVersion? previous, NuGetVersion? current, CancellationToken cancellationToken)
	{
		if (current == null || previous != null && previous >= current) return;
		var range = new VersionRange(previous ?? new NuGetVersion(current.Major, 0, 0), previous == null, current, true);
		var changelogs = await _feedProvider.GetModpackChangelogs(cancellationToken);
		PendingModpackChangelog = changelogs.Where(x => range.Satisfies(x.Version)).OrderByDescending(x => x.Version).ToArray();
	}

	private async Task UpdateModpack(ModpackUpdateInfo info, CancellationToken cancellationToken)
	{
		await _appTaskQueue.EnqueueAndWaitForCompletion(info.UpdateAppTask, cancellationToken);
		_modpackVersionProvider.RefreshInstalledModpackVersion();
	}
}
