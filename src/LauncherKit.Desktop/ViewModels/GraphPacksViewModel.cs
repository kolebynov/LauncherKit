using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

public partial class GraphPacksViewModel : BaseActivatableViewModel, IDisposable
{
	private readonly IInstallAppTaskProvider _installAppTaskProvider;
	private readonly IAppTaskQueue _appTaskQueue;
	private readonly IFeedProvider _feedProvider;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IFeedUtils _feedUtils;
	private readonly IUiDispatcher _dispatcher;
	private readonly IDisposable _appTaskQueueSubscription;

	/// <summary>
	/// Queued tasks of the modpack installation chain; the queue raises events on several threads, so access is locked.
	/// </summary>
	private readonly HashSet<AppTask> _modpackInstallationTasks = [];
	private readonly Lock _modpackInstallationTasksLock = new();

	/// <summary>Names of every graph pack in the feed, including the hidden ones, which may still be installed.</summary>
	private string[] _graphPackNames = [];

	/// <summary>The installed graph pack; only one graph pack can be installed at a time.</summary>
	private string? _installedGraphPackName;

	/// <summary>The graph pack whose install command is running until its task finishes.</summary>
	private string? _installingGraphPackName;

	/// <summary>
	/// Install commands of the cards, disabled while the modpack is being installed and, except for its own card, while another
	/// graph pack is installed or being installed.
	/// </summary>
	private AsyncRelayCommand[] _installCommands = [];

	/// <summary>Uninstall commands of the cards, disabled while the modpack is being installed.</summary>
	private AsyncRelayCommand[] _uninstallCommands = [];

	[ObservableProperty]
	public partial IReadOnlyList<ModCardViewModel> GraphPacks { get; private set; } = [];

	public Loader Loader { get; } = new();

	public GraphPacksViewModel(IInstallAppTaskProvider installAppTaskProvider, IAppTaskQueue appTaskQueue,
		IFeedProvider feedProvider, IModpackVersionProvider modpackVersionProvider, IModOrganizerService modOrganizerService,
		IFeedUtils feedUtils, IUiDispatcher dispatcher)
	{
		_installAppTaskProvider = installAppTaskProvider;
		_appTaskQueue = appTaskQueue;
		_feedProvider = feedProvider;
		_modpackVersionProvider = modpackVersionProvider;
		_modOrganizerService = modOrganizerService;
		_feedUtils = feedUtils;
		_dispatcher = dispatcher;

		_appTaskQueueSubscription = appTaskQueue.Events
			.Subscribe(evt =>
			{
				Action handle = evt switch
				{
					AppTaskQueue.Event.TaskQueued(var t) => () => UpdateModpackInstallationTasks(t, isQueued: true),
					AppTaskQueue.Event.TaskStarted or AppTaskQueue.Event.MicroTaskStarted or AppTaskQueue.Event.TaskFailed =>
						static () => { },
					AppTaskQueue.Event.TaskCompleted(var t) => () => UpdateModpackInstallationTasks(t, isQueued: false),
					AppTaskQueue.Event.TaskRemoved(var t, _) => () => UpdateModpackInstallationTasks(t, isQueued: false),
				};
				handle();
			});

		this.WhenActivated(d =>
		{
			_modpackVersionProvider.ObservePropertyChanged(x => x.InstalledModpackVersion)
				.ObserveOnUi(dispatcher)
				.SubscribeAwait((_, ct) => UpdateGraphPacks(ct))
				.AddTo(d);

			// The modpack installation may install a graph pack, so the installed one is re-read once it ends.
			this.ObservePropertyChanged(x => x.IsModpackInstalling)
				.ObserveOnUi(dispatcher)
				.Subscribe(_ =>
				{
					RefreshInstalledGraphPack();
					foreach (var uninstallCommand in _uninstallCommands)
					{
						uninstallCommand.NotifyCanExecuteChanged();
					}
				})
				.AddTo(d);
		});
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
			_appTaskQueueSubscription.Dispose();
	}

	/// <summary>
	/// Whether any task of the modpack installation chain or the modpack removal is queued. The chain replaces Mod Organizer in
	/// its <c>InstallModpack</c> task and installs the chosen graph pack in its last task, so the cards stay locked until the
	/// whole chain is done; the removal deletes Mod Organizer. A failed task stays queued until it is resumed or
	/// cancelled.
	/// </summary>
	[ObservableProperty]
	private partial bool IsModpackInstalling { get; set; }

	/// <remarks>
	/// A set rather than a counter: a task skipped as unneeded is reported as removed and may later be reported as
	/// completed too.
	/// </remarks>
	private void UpdateModpackInstallationTasks(AppTask task, bool isQueued)
	{
		if (!AppTaskProvider.IsModpackInstallationTask(task.Id))
		{
			return;
		}

		lock (_modpackInstallationTasksLock)
		{
			if (isQueued)
			{
				_modpackInstallationTasks.Add(task);
			}
			else
			{
				_modpackInstallationTasks.Remove(task);
			}

			var isInstalling = _modpackInstallationTasks.Count > 0;
			_dispatcher.Post(() => IsModpackInstalling = isInstalling);
		}
	}

	private async ValueTask UpdateGraphPacks(CancellationToken cancellationToken)
	{
		using var loading = Loader.BeginLoading();
		var modpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		var allGraphPacks = await _feedProvider.GetGraphPacks(cancellationToken);
		_graphPackNames = allGraphPacks.Select(x => x.Name).ToArray();
		_installedGraphPackName = FindInstalledGraphPack();
		var graphPacks = allGraphPacks.Where(x => x.IsOfferedFor(modpackVersion)).ToArray();
		_installCommands = new AsyncRelayCommand[graphPacks.Length];
		_uninstallCommands = new AsyncRelayCommand[graphPacks.Length];
		var cards = new ModCardViewModel[graphPacks.Length];
		for (var i = 0; i < graphPacks.Length; i++)
		{
			var graphPack = graphPacks[i];
			var availableItem = modpackVersion != null ? graphPack.FindLatestSupportedItem(modpackVersion) : null;
			_installCommands[i] = availableItem != null
				? new AsyncRelayCommand(ct => InstallGraphPack(graphPack, availableItem, ct),
					() => !IsModpackInstalling && GetOtherGraphPackReason(graphPack.Name) == null)
				: new AsyncRelayCommand(() => Task.CompletedTask, () => false);
			_uninstallCommands[i] = new AsyncRelayCommand(ct => UninstallGraphPack(graphPack, ct),
				() => !IsModpackInstalling);
			cards[i] = new ModCardViewModel(
				DisplayName(graphPack), _feedUtils.GetUri(graphPack.LogoPath), _feedUtils.GetUri(graphPack.DescriptionPath.Resolve()),
				() => _installAppTaskProvider.GetModInstallState(graphPack, modpackVersion), _installCommands[i],
				_uninstallCommands[i],
				() => IsModpackInstalling
					? new ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued()
					: availableItem == null
						? new ModCardViewModel.InstallUnavailableReason.Updating()
						: GetOtherGraphPackReason(graphPack.Name) ?? new ModCardViewModel.InstallUnavailableReason.Updating(),
				installRequiresNewGame: true, uninstallRequiresNewGame: true);
		}

		GraphPacks = cards;
	}

	/// <summary>
	/// Why <paramref name="graphPackName"/> cannot be installed because of another graph pack, or
	/// <see langword="null"/> when no other graph pack is installed or being installed.
	/// </summary>
	private ModCardViewModel.InstallUnavailableReason? GetOtherGraphPackReason(string graphPackName)
	{
		if (_installedGraphPackName != null && _installedGraphPackName != graphPackName)
		{
			return new ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled(_installedGraphPackName);
		}

		return _installingGraphPackName != null && _installingGraphPackName != graphPackName
			? new ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled(_installingGraphPackName)
			: null;
	}

	private static string DisplayName(GraphPackFeedData graphPack) => graphPack.DisplayName?.Resolve() ?? graphPack.Name;

	private string? FindInstalledGraphPack() => _graphPackNames.FirstOrDefault(_modOrganizerService.IsModInstalled);

	private void RefreshInstalledGraphPack()
	{
		_installedGraphPackName = FindInstalledGraphPack();
		NotifyInstallCommands();
	}

	private void NotifyInstallCommands()
	{
		foreach (var installCommand in _installCommands)
		{
			installCommand.NotifyCanExecuteChanged();
		}
	}

	private async Task InstallGraphPack(GraphPackFeedData graphPack, ModFeedItem feedItem,
		CancellationToken cancellationToken)
	{
		_installingGraphPackName = graphPack.Name;
		NotifyInstallCommands();
		try
		{
			var appTask = await _installAppTaskProvider.CreateModInstallAppTask(graphPack, feedItem.Version, cancellationToken);
			await _appTaskQueue.EnqueueAndWaitForCompletion(appTask, cancellationToken);
		}
		finally
		{
			_installingGraphPackName = null;
			RefreshInstalledGraphPack();
		}
	}

	private async Task UninstallGraphPack(GraphPackFeedData graphPack, CancellationToken cancellationToken)
	{
		try
		{
			await _appTaskQueue.EnqueueAndWaitForCompletion(
				_installAppTaskProvider.CreateModUninstallAppTask(graphPack), cancellationToken);
		}
		finally
		{
			RefreshInstalledGraphPack();
		}
	}
}
