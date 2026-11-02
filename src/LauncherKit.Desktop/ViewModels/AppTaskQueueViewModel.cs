using System.Collections.ObjectModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class AppTaskQueueViewModel : ViewModelBase, IDisposable
{
	private readonly IAppLogsCollector _appLogsCollector;
	private readonly IAppTaskQueue _appTaskQueue;
	private readonly IDisposable _appTaskQueueSubscription;
	private readonly IUiDispatcher _dispatcher;

	public ObservableCollection<AppTaskViewModel> AppTasks { get; } = new();

	public AppTaskQueueViewModel(IAppLogsCollector appLogsCollector, IAppTaskQueue appTaskQueue, IUiDispatcher dispatcher)
	{
		_appLogsCollector = appLogsCollector;
		_appTaskQueue = appTaskQueue;
		_dispatcher = dispatcher;

		_appTaskQueueSubscription = _appTaskQueue.Events
			.ObserveOnUi(dispatcher)
			.Subscribe(evt =>
			{
				Action handle = evt switch
				{
					AppTaskQueue.Event.TaskQueued(var t) => () => EnsureAppTaskViewModelAdded(t),
					AppTaskQueue.Event.TaskStarted => static () => { },
					AppTaskQueue.Event.MicroTaskStarted(var t, var mt, var c) =>
						() => EnsureAppTaskViewModelAdded(t).SetRunningMicroTask(mt.Description, c),
					AppTaskQueue.Event.TaskFailed(var t, var e) => () => EnsureAppTaskViewModelAdded(t).SetError(e),
					AppTaskQueue.Event.TaskCompleted(var t) => () =>
					{
						var appTaskViewModel = EnsureAppTaskViewModelAdded(t);
						appTaskViewModel.Complete();
						AppTasks.Remove(appTaskViewModel);
					},
					AppTaskQueue.Event.TaskRemoved(var t, _) => () =>
					{
						var appTaskViewModel = EnsureAppTaskViewModelAdded(t);
						appTaskViewModel.Complete();
						AppTasks.Remove(appTaskViewModel);
					},
				};
				handle();
			});
	}

	public Task CancelAppTask(AppTaskViewModel appTaskViewModel, CancellationToken cancellationToken) =>
		_appTaskQueue.CancelAppTask(appTaskViewModel.AppTask.Id, cancellationToken);

	public void Dispose()
	{
		_appTaskQueueSubscription.Dispose();
	}

	[RelayCommand]
	private void Resume() => _appTaskQueue.Resume();

	private AppTaskViewModel EnsureAppTaskViewModelAdded(AppTask appTask)
	{
		var appTaskViewModel = AppTasks.FirstOrDefault(x => x.AppTask == appTask);
		if (appTaskViewModel != null)
		{
			return appTaskViewModel;
		}

		AppTasks.Add(appTaskViewModel = new AppTaskViewModel(appTask, _appLogsCollector, _dispatcher));
		return appTaskViewModel;
	}

}