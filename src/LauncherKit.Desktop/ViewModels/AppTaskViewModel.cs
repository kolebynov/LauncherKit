using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.ViewModels;

#pragma warning disable CA1001
public sealed partial class AppTaskViewModel : ViewModelBase
#pragma warning restore CA1001
{
	private readonly IAppLogsCollector _appLogsCollector;
	private readonly SerialDisposable _currentMicroTaskSubscription = new();

	[ObservableProperty]
	public partial Exception? Error { get; private set; }

	public AppTask AppTask { get; }

	[ObservableProperty]
	public partial MicroTaskDescription? RunningMicroTaskDescription { get; private set; }

	[ObservableProperty]
	public partial ViewModelBase? ProgressViewModel { get; private set; }

	[ObservableProperty]
	public partial ChoiceSelectionViewModel? ChoiceSelection { get; private set; }

	private readonly IUiDispatcher _dispatcher;

	public AppTaskViewModel(AppTask appTask, IAppLogsCollector appLogsCollector, IUiDispatcher dispatcher)
	{
		AppTask = appTask;
		_appLogsCollector = appLogsCollector;
		_dispatcher = dispatcher;
	}

	public void SetRunningMicroTask(MicroTaskDescription? description, Observable<MicroTaskStep> stepObservable)
	{
		_currentMicroTaskSubscription.Disposable = Disposable.Empty;
		RunningMicroTaskDescription = description;
		ProgressViewModel = description != null ? UnknownProgressViewModel.Instance : null;
		ChoiceSelection = null;

		if (description != null)
		{
			Error = null;
			_currentMicroTaskSubscription.Disposable = stepObservable.ObserveOnUi(_dispatcher)
				.SubscribeSafe(step =>
				{
					ChoiceSelection = step is MicroTaskStep.SelectChoices(var context)
						? new ChoiceSelectionViewModel(context)
						: null;
					ProgressViewModel = step switch
					{
						MicroTaskStep.Progress(IDownloadProgress downloadProgress) =>
							new DownloadFileProgressViewModel(downloadProgress, _dispatcher),
						MicroTaskStep.Progress(var operationProgress) => new OperationProgressViewModel(operationProgress, _dispatcher),
						MicroTaskStep.MicroOperation(var operation) => new UnknownProgressViewModel { Operation = operation },
						MicroTaskStep.SelectChoices => UnknownProgressViewModel.Instance,
					};
				});
		}
	}

	public void SetError(Exception exception)
	{
		SetRunningMicroTask(null, Observable.Empty<MicroTaskStep>());
		Error = exception;
	}

	public void Complete()
	{
		SetRunningMicroTask(null, Observable.Empty<MicroTaskStep>());
	}

	[RelayCommand]
	private Task CollectLogs() => _appLogsCollector.CollectLogs(CancellationToken.None);

}

public sealed class UnknownProgressViewModel : ViewModelBase
{
	public static UnknownProgressViewModel Instance { get; } = new();
	public MicroTaskDescription? Operation { get; init; }
}

public partial class OperationProgressViewModel : ViewModelBase
{
	private readonly IUiDispatcher _dispatcher;
	public MicroTaskDescription Operation { get; }

	[ObservableProperty]
	public partial double Progress { get; private set; }

	public OperationProgressViewModel(IOperationProgress operationProgress, IUiDispatcher dispatcher)
	{
		_dispatcher = dispatcher;
		Operation = operationProgress.Operation;
		Progress = operationProgress.Progress;
		operationProgress.ProgressObservable
			.ObserveOnUi(_dispatcher)
			.SubscribeSafe(_ =>
			{
				Progress = operationProgress.Progress;
			});
	}
}

public sealed partial class DownloadFileProgressViewModel : OperationProgressViewModel
{
	private readonly IDownloadProgress _downloadProgress;

	[ObservableProperty]
	public partial long DownloadedBytes { get; private set; }

	[ObservableProperty]
	public partial long TotalBytes { get; private set; }

	[ObservableProperty]
	public partial double Speed { get; private set; }

	[ObservableProperty]
	public partial bool IsPaused { get; private set; }

	public DownloadFileProgressViewModel(IDownloadProgress downloadProgress, IUiDispatcher dispatcher)
		:base(downloadProgress, dispatcher)
	{
		_downloadProgress = downloadProgress;
		DownloadedBytes = _downloadProgress.DownloadedBytes;
		TotalBytes = _downloadProgress.TotalBytes;
		Speed = _downloadProgress.Speed;

		_downloadProgress.ProgressObservable
			.ObserveOnUi(dispatcher)
			.SubscribeSafe(_ =>
			{
				DownloadedBytes = _downloadProgress.DownloadedBytes;
				TotalBytes = _downloadProgress.TotalBytes;
				Speed = _downloadProgress.Speed;
				IsPaused = _downloadProgress.IsPaused;
			});
	}

	[RelayCommand]
	private Task TogglePause() =>
		IsPaused ? _downloadProgress.Resume().AsTask() : _downloadProgress.Pause().AsTask();
}
