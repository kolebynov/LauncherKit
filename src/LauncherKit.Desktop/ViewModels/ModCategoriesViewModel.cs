using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// The feed's mod categories, in feed order, each a section of mod cards. Tracks the modpack installation chain, which
/// locks the cards of every category.
/// </summary>
public partial class ModCategoriesViewModel : BaseActivatableViewModel, IDisposable
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

	/// <summary>
	/// The mod being installed per exclusive category ID, kept here rather than in the category view models, which are
	/// recreated on each activation, so that a pending install keeps locking its category after navigating away and back.
	/// Accessed on the UI thread only.
	/// </summary>
	internal Dictionary<string, string> InstallingExclusiveMods { get; } = [];

	[ObservableProperty]
	public partial IReadOnlyList<ModCategoryViewModel> Categories { get; private set; } = [];

	/// <summary>
	/// Whether any task of the modpack installation chain or the modpack removal is queued. The chain replaces Mod Organizer in
	/// its <c>InstallModpack</c> task and installs the chosen components in its last tasks, so the cards stay locked until the
	/// whole chain is done; the removal deletes Mod Organizer. A failed task stays queued until it is resumed or
	/// cancelled.
	/// </summary>
	[ObservableProperty]
	public partial bool IsModpackInstalling { get; private set; }

	public Loader Loader { get; } = new();

	public ModCategoriesViewModel(IInstallAppTaskProvider installAppTaskProvider, IAppTaskQueue appTaskQueue,
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

		this.WhenActivated(async (_, ct) => await LoadCategories(ct));
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

	private async ValueTask LoadCategories(CancellationToken cancellationToken)
	{
		using var loading = Loader.BeginLoading();
		var categories = await _feedProvider.GetModCategories(cancellationToken);
		Categories = categories
			.Select(x => new ModCategoryViewModel(x, this, _installAppTaskProvider, _appTaskQueue, _feedProvider,
				_modpackVersionProvider, _modOrganizerService, _feedUtils, _dispatcher))
			.ToArray();
	}

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
}
