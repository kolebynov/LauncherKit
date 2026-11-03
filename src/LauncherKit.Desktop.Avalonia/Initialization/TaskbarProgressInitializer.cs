using R3;
using LauncherKit.Desktop.Avalonia.Interop;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Initialization;
using LauncherKit.Core.Models;

namespace LauncherKit.Desktop.Avalonia.Initialization;

public sealed class TaskbarProgressInitializer : IConfiguredScopeInitializer, IDisposable
{
	private const int ProgressMaximum = 1000;

	private readonly IAppTaskQueue _appTaskQueue;
	private readonly ITaskbarProgressSink _taskbarProgressSink;
	private IDisposable? _queueSubscription;

	public TaskbarProgressInitializer(IAppTaskQueue appTaskQueue, ITaskbarProgressSink taskbarProgressSink)
	{
		_appTaskQueue = appTaskQueue;
		_taskbarProgressSink = taskbarProgressSink;
	}

	public ValueTask Initialize()
	{
		if (!OperatingSystem.IsWindows() || _queueSubscription != null)
		{
			return ValueTask.CompletedTask;
		}

		_queueSubscription = SubscribeToEvents();
		return ValueTask.CompletedTask;
	}

	public void Dispose()
	{
		_queueSubscription?.Dispose();
		SetProgress(TaskbarProgressBarState.NoProgress, 0);
	}

	private IDisposable SubscribeToEvents()
	{
#pragma warning disable CA2000
		var microTasksSubscription = new SerialDisposable();
#pragma warning restore CA2000

		var appTaskQueueSubscription = _appTaskQueue.Events.Subscribe(evt =>
		{
			Action handle = evt switch
			{
				AppTaskQueue.Event.TaskQueued or AppTaskQueue.Event.TaskStarted => static () => { },
				AppTaskQueue.Event.MicroTaskStarted(_, _, var steps) => () =>
				{
					SetIndeterminateState();
					var compositeDisposable = new CompositeDisposable();
					var stepsSub = steps.Subscribe(step =>
					{
						var operationProgress = step switch
						{
							MicroTaskStep.Progress(var progress) => progress,
							MicroTaskStep.MicroOperation or MicroTaskStep.SelectChoices => null,
						};
						if (operationProgress != null)
						{
							compositeDisposable.Add(SubscribeOperationProgress(operationProgress));
						}
					});
					compositeDisposable.Add(stepsSub);
					microTasksSubscription.Disposable = compositeDisposable;
				},
				AppTaskQueue.Event.TaskFailed => () =>
				{
					microTasksSubscription.Disposable = null;
					SetErrorState();
				},
				AppTaskQueue.Event.TaskCompleted => () =>
				{
					microTasksSubscription.Disposable = null;
					ClearTaskbarState();
				},
				AppTaskQueue.Event.TaskRemoved(_, var index) => () =>
				{
					if (index == 0)
					{
						microTasksSubscription.Disposable = null;
						ClearTaskbarState();
					}
				},
			};
			handle();
		});

		return Disposable.Combine(appTaskQueueSubscription, microTasksSubscription);
	}

	private IDisposable SubscribeOperationProgress(IOperationProgress operationProgress)
	{
		RefreshProgress(operationProgress);
		return operationProgress.ProgressObservable.Subscribe(_ => RefreshProgress(operationProgress));
	}

	private void RefreshProgress(IOperationProgress operationProgress)
	{
		if (operationProgress.Progress <= 0)
		{
			SetProgress(TaskbarProgressBarState.Indeterminate, 0);
			return;
		}

		var ratio = Math.Clamp(operationProgress.Progress, 0d, 1d);
		var current = (int)Math.Round(ratio * ProgressMaximum);
		SetProgress(TaskbarProgressBarState.Normal, current);
	}

	private void SetIndeterminateState() => SetProgress(TaskbarProgressBarState.Indeterminate, 0);

	private void SetErrorState() => SetProgress(TaskbarProgressBarState.Error, ProgressMaximum);

	private void ClearTaskbarState() => SetProgress(TaskbarProgressBarState.NoProgress, 0);

	private void SetProgress(TaskbarProgressBarState state, int current) =>
		_taskbarProgressSink.SetProgress(state, (ulong)Math.Clamp(current, 0, ProgressMaximum), ProgressMaximum);
}