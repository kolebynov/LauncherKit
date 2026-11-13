using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Update;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

/// <inheritdoc cref="IInstallationService"/>
/// <remarks>
/// Scoped with the queue and subscribed to it from construction, so it sees every task of the modpack installation chain,
/// including the one a game launch queues as a dependency. The queue raises events on several threads, so the state is
/// locked. Refreshes of the installed modpack version (which may write files, e.g. a consumer's version file) are
/// serialized by a separate lock, always taken before the state lock.
/// </remarks>
public sealed class InstallationService : IInstallationService, IDisposable
{
	private readonly IAppTaskQueue _appTaskQueue;
	private readonly IAppTaskProvider _appTaskProvider;
	private readonly IInstallAppTaskProvider _installAppTaskProvider;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly Subject<InstallationChange> _changes = new();
	private readonly IDisposable _appTaskQueueSubscription;
	private readonly Lock _lock = new();
	private readonly Lock _refreshLock = new();
	private readonly List<QueuedInstallation> _queuedInstallations = [];

	/// <summary>
	/// Queued tasks of the modpack installation chain; a set rather than a counter, as a task skipped as unneeded is
	/// reported as removed and may later be reported as completed too.
	/// </summary>
	private readonly HashSet<AppTask> _modpackTasks = [];

	public InstallationService(IAppTaskQueue appTaskQueue, IAppTaskProvider appTaskProvider,
		IInstallAppTaskProvider installAppTaskProvider, IModpackVersionProvider modpackVersionProvider)
	{
		_appTaskQueue = appTaskQueue;
		_appTaskProvider = appTaskProvider;
		_installAppTaskProvider = installAppTaskProvider;
		_modpackVersionProvider = modpackVersionProvider;
		_appTaskQueueSubscription = appTaskQueue.Events.Subscribe(evt =>
		{
			switch (evt)
			{
				case AppTaskQueue.Event.TaskQueued(var t):
					UpdateModpackTasks(t, isQueued: true);
					break;
				case AppTaskQueue.Event.TaskCompleted(var t):
					UpdateModpackTasks(t, isQueued: false);
					break;
				case AppTaskQueue.Event.TaskRemoved(var t, _):
					UpdateModpackTasks(t, isQueued: false);
					break;
				case AppTaskQueue.Event.TaskStarted or AppTaskQueue.Event.MicroTaskStarted or AppTaskQueue.Event.TaskFailed:
					break;
			}
		});
	}

	public IReadOnlyList<QueuedInstallation> QueuedInstallations
	{
		get
		{
			lock (_lock)
			{
				return _queuedInstallations.ToArray();
			}
		}
	}

	public Observable<InstallationChange> Changes => _changes;

	public ModInstallState GetModInstallState(ModFeedData mod) =>
		_installAppTaskProvider.GetModInstallState(mod, _modpackVersionProvider.InstalledModpackVersion);

	public async Task InstallMod(ModFeedData mod, ModCategoryFeedData category, NuGetVersion? version,
		CancellationToken cancellationToken)
	{
		var installation = new QueuedInstallation.ModInstall(mod);
		if (!TryReserve(installation, mod, category.IsExclusive ? category : null))
			return;
		try
		{
			var appTask = await _installAppTaskProvider.CreateModInstallAppTask(mod, version, cancellationToken);
			await _appTaskQueue.EnqueueAndWaitForCompletion(appTask, cancellationToken);
		}
		finally
		{
			Release(installation);
		}
	}

	public async Task UninstallMod(ModFeedData mod, CancellationToken cancellationToken)
	{
		var installation = new QueuedInstallation.ModUninstall(mod);
		if (!TryReserve(installation, mod, exclusiveCategory: null))
			return;
		try
		{
			await _appTaskQueue.EnqueueAndWaitForCompletion(_installAppTaskProvider.CreateModUninstallAppTask(mod),
				cancellationToken);
		}
		finally
		{
			Release(installation);
		}
	}

	public async Task InstallModpack(ModpackUpdateInfo info, CancellationToken cancellationToken)
	{
		try
		{
			await _appTaskQueue.EnqueueAndWaitForCompletion(info.UpdateAppTask, cancellationToken);
		}
		finally
		{
			RefreshModpackVersion();
		}
	}

	public async Task UninstallModpack(CancellationToken cancellationToken)
	{
		try
		{
			await _appTaskQueue.EnqueueAndWaitForCompletion(_appTaskProvider.CreateUninstallModpackTask(), cancellationToken);
		}
		finally
		{
			RefreshModpackVersion();
		}
	}

	public async Task RunGame(GameLaunch launch, CancellationToken cancellationToken) =>
		await _appTaskQueue.EnqueueAndWaitForCompletion(await _appTaskProvider.CreateRunGameTask(launch, cancellationToken),
			cancellationToken);

	public void Dispose()
	{
		_appTaskQueueSubscription.Dispose();
		_changes.Dispose();
	}

	/// <summary>
	/// Reserves <paramref name="installation"/> before its task is created, unless the mod is already queued (a second
	/// request must neither queue a duplicate nor release the first one's reservation) or, for an install in
	/// <paramref name="exclusiveCategory"/>, another mod of it is already reserved or queued for install.
	/// </summary>
	private bool TryReserve(QueuedInstallation installation, ModFeedData mod, ModCategoryFeedData? exclusiveCategory)
	{
		lock (_lock)
		{
			if (_queuedInstallations.Any(x => x is QueuedInstallation.ModInstall(var m) && m.Name == mod.Name ||
				    x is QueuedInstallation.ModUninstall(var u) && u.Name == mod.Name))
				return false;
			if (exclusiveCategory != null && _queuedInstallations.Any(x =>
				    x is QueuedInstallation.ModInstall(var m) && m.CategoryId == exclusiveCategory.Id))
				return false;
			_queuedInstallations.Add(installation);
			_changes.OnNext(new InstallationChange.Queued(installation));
			return true;
		}
	}

	private void RefreshModpackVersion()
	{
		lock (_refreshLock)
		{
			_modpackVersionProvider.RefreshInstalledModpackVersion();
		}
	}

	private void Release(QueuedInstallation installation)
	{
		lock (_lock)
		{
			_queuedInstallations.RemoveAll(x => ReferenceEquals(x, installation));
			_changes.OnNext(new InstallationChange.Finished(installation));
		}
	}

	private void UpdateModpackTasks(AppTask task, bool isQueued)
	{
		if (!AppTaskProvider.IsModpackInstallationTask(task.Id))
			return;

		lock (_refreshLock)
		lock (_lock)
		{
			var wasQueued = _modpackTasks.Count > 0;
			if (isQueued)
				_modpackTasks.Add(task);
			else
				_modpackTasks.Remove(task);
			var isModpackQueued = _modpackTasks.Count > 0;
			if (isModpackQueued == wasQueued)
				return;

			if (isModpackQueued)
			{
				_queuedInstallations.Add(QueuedInstallation.Modpack.Instance);
				_changes.OnNext(new InstallationChange.Queued(QueuedInstallation.Modpack.Instance));
			}
			else
			{
				// The chain may have changed mods without changing the modpack version, so this is raised regardless.
				_modpackVersionProvider.RefreshInstalledModpackVersion();
				_queuedInstallations.Remove(QueuedInstallation.Modpack.Instance);
				_changes.OnNext(new InstallationChange.Finished(QueuedInstallation.Modpack.Instance));
			}
		}
	}
}
