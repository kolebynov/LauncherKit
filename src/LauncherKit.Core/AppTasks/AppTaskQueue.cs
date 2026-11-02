using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks;

public sealed class AppTaskQueue : IAppTaskQueue
{
	private static readonly PropertyChangedEventArgs TaskCountChangedEventArgs = new(nameof(TaskCount));

	private readonly LockedResource<ProcessingContext> _contextResource;
	private readonly ILogger<AppTaskQueue> _logger;
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly CancellationTokenSource _cancellationTokenSource = new();
	private readonly SemaphoreSlim _newItemsInQueueSemaphore = new(0, 1);
	private readonly SemaphoreSlim _processingPauseSemaphore = new(0, 1);
	private readonly SemaphoreSlim _enqueueSemaphore = new(1, 1);
	private readonly ReplaySubject<Event> _eventSubject = new(10);
	private readonly Task _processingTask;
	private AppTask? _lastFailedTask;
	private int _taskCount;

	public event PropertyChangedEventHandler? PropertyChanged;

	public Observable<Event> Events => _eventSubject;

	public int TaskCount => Volatile.Read(ref _taskCount);

	public AppTaskQueue(ILogger<AppTaskQueue> logger, IServiceScopeFactory serviceScopeFactory, TimeProvider timeProvider)
	{
		_logger = logger;
		_serviceScopeFactory = serviceScopeFactory;
		_timeProvider = timeProvider;
#pragma warning disable CA2000
		_contextResource = new LockedResource<ProcessingContext>(new ProcessingContext(_eventSubject, _logger));
#pragma warning restore CA2000

		_processingTask = Task.Run(() => ProcessQueue(_cancellationTokenSource.Token));
	}

	public async Task EnqueueAndWaitForCompletion(AppTask task, CancellationToken cancellationToken)
	{
		var enqueueResult = await Enqueue(task, cancellationToken);

		if (enqueueResult is AppTaskEnqueueResult.Enqueued(var completionTask))
		{
			await completionTask;
		}
	}

	public async ValueTask<AppTaskEnqueueResult> Enqueue(AppTask task, CancellationToken cancellationToken)
	{
		using var _ = await _enqueueSemaphore.AcquireAsync(cancellationToken);
		_logger.LogInformation("Trying to enqueue app task {AppTaskId}", task.Id);

		var tasksToRun = new List<QueuedTask>(4);
		await AddWithDependencies(task, tasksToRun, new HashSet<AppTask>(), cancellationToken);

		var taskQueued = false;
		using (var contextLock = await _contextResource.AcquireAsync(cancellationToken))
		{
			var context = contextLock.Resource;
			foreach (var taskToRun in tasksToRun)
			{
				if (await (taskToRun.AppTask.CompleteVerifier?.IsTaskComplete(cancellationToken) ?? ValueTask.FromResult(false)) ||
				    !context.Enqueue(taskToRun))
				{
					_logger.LogInformation("App task {AppTaskId} is already complete or in queue", taskToRun.AppTask.Id);
					continue;
				}

				_logger.LogInformation("App task {AppTaskId} queued", taskToRun.AppTask.Id);
				_eventSubject.OnNext(new Event.TaskQueued(taskToRun.AppTask));
				taskQueued = true;
			}

			if (context.Contains(task))
			{
				context.MarkRequested(task);
			}

			UpdateTaskCount(context);
		}

		if (taskQueued && _newItemsInQueueSemaphore.CurrentCount == 0)
		{
			_newItemsInQueueSemaphore.Release();
		}

		return taskQueued ? new AppTaskEnqueueResult.Enqueued(tasksToRun[^1].Tcs.Task) : AppTaskEnqueueResult.NotEnqueued.Instance;
	}

	/// <summary>
	/// Adds <paramref name="task"/> after its incomplete dependencies, visiting them depth-first in the order they are
	/// listed, so an earlier dependency and everything it depends on run before a later one.
	/// </summary>
	private async ValueTask AddWithDependencies(AppTask task, List<QueuedTask> tasksToRun, HashSet<AppTask> visitedTasks,
		CancellationToken cancellationToken)
	{
		if (!visitedTasks.Add(task))
		{
			return;
		}

		if (await (task.CompleteVerifier?.IsTaskComplete(cancellationToken) ?? ValueTask.FromResult(false)))
		{
			_logger.LogInformation("App task {AppTaskId} is already complete", task.Id);
			return;
		}

		foreach (var dependency in task.Dependencies)
		{
			await AddWithDependencies(dependency, tasksToRun, visitedTasks, cancellationToken);
		}

		tasksToRun.Add(new QueuedTask(task, new TaskCompletionSource()));
	}

	public void Resume()
	{
		if (_processingPauseSemaphore.CurrentCount == 0)
		{
			_processingPauseSemaphore.Release();
		}
	}

	public async Task CancelAppTask(string appTaskId, CancellationToken cancellationToken)
	{
		using var contextLock = await _contextResource.AcquireAsync(cancellationToken);
		var context = contextLock.Resource;
		if (context.ProcessingAppTask is { } processingAppTask && processingAppTask.QueuedTask.AppTask.Id == appTaskId)
		{
			processingAppTask.Cancel();
			if (await processingAppTask.RunningTask is AppTaskResult.Success)
			{
				return;
			}
		}

		context.Remove(appTaskId);
		UpdateTaskCount(context);

		if (_lastFailedTask != null && !context.Contains(_lastFailedTask))
		{
			Resume();
		}
	}

	public async ValueTask DisposeAsync()
	{
		_cancellationTokenSource.Cancel();
		try
		{
			await _processingTask.WaitAsync(TimeSpan.FromSeconds(1), _timeProvider);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning("App task processing did not stop within 1 second");
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Error while cancelling app task processing");
		}

		_cancellationTokenSource.Dispose();
		_enqueueSemaphore.Dispose();
		_newItemsInQueueSemaphore.Dispose();
		_processingPauseSemaphore.Dispose();
		_eventSubject.Dispose();
		_contextResource.Dispose();
	}

	private async Task ProcessQueue(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting app task processing");

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await _newItemsInQueueSemaphore.WaitAsync(cancellationToken);
				await ProcessNewQueueItems(cancellationToken);
			}

			_logger.LogInformation("App task processing was cancelled");
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("App task processing was cancelled");
		}
	}

	private async Task ProcessNewQueueItems(CancellationToken cancellationToken)
	{
		while (true)
		{
			ExecutingAppTask processingAppTask;
			using (var contextLock = await _contextResource.AcquireAsync(cancellationToken))
			{
				if (!contextLock.Resource.TryStartNewAppTask(ProcessAppTask, out var p, cancellationToken))
				{
					break;
				}

				processingAppTask = p.Value;
			}

			var processingResult = await processingAppTask.RunningTask;
			var (additionalCleanup, continuation) = processingResult switch
			{
				AppTaskResult.Success => OnSuccess(),
				AppTaskResult.Canceled => OnCanceled(),
				AppTaskResult.Error(var e) => OnError(e),
			};

			using (var contextLock = await _contextResource.AcquireAsync(cancellationToken))
			{
				contextLock.Resource.ResetProcessingAppTask();
				additionalCleanup?.Invoke(contextLock.Resource);
				UpdateTaskCount(contextLock.Resource);
			}

			await (continuation?.Invoke(cancellationToken) ?? Task.CompletedTask);

			(Action<ProcessingContext>?, Func<CancellationToken, Task>?) OnSuccess()
			{
				processingAppTask.QueuedTask.Tcs.TrySetResult();
				_eventSubject.OnNext(new Event.TaskCompleted(processingAppTask.QueuedTask.AppTask));
				return (c =>
				{
					c.Forget(processingAppTask.QueuedTask.AppTask);
					c.SkipUnneeded();
				}, null);
			}

			(Action<ProcessingContext>?, Func<CancellationToken, Task>?) OnCanceled()
			{
				_logger.LogWarning("App task {AppTaskId} was cancelled", processingAppTask.QueuedTask.AppTask.Id);
				processingAppTask.QueuedTask.Tcs.TrySetResult();
				_eventSubject.OnNext(new Event.TaskCompleted(processingAppTask.QueuedTask.AppTask));
				return (c => c.Forget(processingAppTask.QueuedTask.AppTask), null);
			}

			(Action<ProcessingContext>?, Func<CancellationToken, Task>?) OnError(Exception e) => (
				c => c.PushFront(processingAppTask.QueuedTask),
				async ct =>
				{
					_logger.LogError(e, "Error while executing app task {AppTaskId}. Pausing processing...", processingAppTask.QueuedTask.AppTask.Id);
					_processingPauseSemaphore.Wait(0, ct);
					var waitTask = _processingPauseSemaphore.WaitAsync(ct);
					_eventSubject.OnNext(new Event.TaskFailed(processingAppTask.QueuedTask.AppTask, e));
					_lastFailedTask = processingAppTask.QueuedTask.AppTask;
					await waitTask;
					_logger.LogInformation("Resuming app task processing");
				});
		}
	}

	private async Task ProcessAppTask(AppTask appTask, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting app task: {AppTaskId}", appTask.Id);
		_eventSubject.OnNext(new Event.TaskStarted(appTask));

		using var scope = _serviceScopeFactory.CreateScope();
		var context = new MicroTasksProviderContext(scope.ServiceProvider.GetRequiredService<IMicroTasksFactory>(),
			scope.ServiceProvider);
		foreach (var appMicroTask in appTask.MicroTasksProvider(context))
		{
			await ProcessAppMicroTask(appTask, appMicroTask, cancellationToken);
		}

		await (appTask.OnComplete?.Invoke(cancellationToken) ?? ValueTask.CompletedTask);
		_logger.LogInformation("App task {AppTaskId} completed", appTask.Id);
	}

	private async ValueTask ProcessAppMicroTask(AppTask appTask, IAppMicroTask appMicroTask, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting app micro task: {AppMicroTaskDescription}", appMicroTask.Description.Value);

		using var stepSubject = new BehaviorSubject<MicroTaskStep>(new MicroTaskStep.MicroOperation(null));
		_eventSubject.OnNext(new Event.MicroTaskStarted(appTask, appMicroTask, stepSubject));
		await appMicroTask.Execute(stepSubject, cancellationToken);

		_logger.LogInformation("App micro task {AppMicroTaskDescription} completed", appMicroTask.Description.Value);
	}

	/// <summary>
	/// Called while holding the context lock, so notifications are raised in the order the count changes.
	/// </summary>
	private void UpdateTaskCount(ProcessingContext context)
	{
		var taskCount = context.VisibleTaskCount;
		if (taskCount == _taskCount)
		{
			return;
		}

		Volatile.Write(ref _taskCount, taskCount);
		PropertyChanged?.Invoke(this, TaskCountChangedEventArgs);
	}

	public union Event(Event.TaskQueued, Event.TaskStarted, Event.MicroTaskStarted, Event.TaskFailed, Event.TaskCompleted, Event.TaskRemoved)
	{
		public sealed record TaskQueued(AppTask Task);

		public sealed record TaskStarted(AppTask Task);

		public sealed record MicroTaskStarted(AppTask Task, IAppMicroTask MicroTask, Observable<MicroTaskStep> StepObservable);

		public sealed record TaskFailed(AppTask Task, Exception Exception);

		public sealed record TaskCompleted(AppTask Task);

		public sealed record TaskRemoved(AppTask Task, int Index);
	}

	private union AppTaskResult(AppTaskResult.Success, AppTaskResult.Canceled, AppTaskResult.Error)
	{
		public sealed record Success;

		public sealed record Canceled;

		public sealed record Error(Exception Exception);
	}

	private readonly record struct QueuedTask(AppTask AppTask, TaskCompletionSource Tcs);

	private readonly struct ExecutingAppTask : IDisposable
	{
		private readonly CancellationTokenSource _cts;
		private readonly CancellationTokenSource _linkedCts;

		public QueuedTask QueuedTask { get; }

		public Task<AppTaskResult> RunningTask { get; }

		public void Cancel()
		{
			_cts.Cancel();
		}

		public void Dispose()
		{
			_cts.Dispose();
			_linkedCts.Dispose();
		}

		public static ExecutingAppTask Create(QueuedTask queuedTask, Func<AppTask, CancellationToken, Task> startTaskFunc,
			CancellationToken cancellationToken)
		{
			var cts = new CancellationTokenSource();
			var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
			var runningTask = StartAppTask(queuedTask.AppTask, startTaskFunc, cts, linkedCts);
			return new ExecutingAppTask(queuedTask, runningTask, cts, linkedCts);

			static async Task<AppTaskResult> StartAppTask(AppTask appTask, Func<AppTask, CancellationToken, Task> startTaskFunc,
				CancellationTokenSource cts, CancellationTokenSource linkedCts)
			{
				try
				{
					await startTaskFunc(appTask, linkedCts.Token);
					return AppTaskResult.Success.Instance;
				}
				catch (Exception e)
				{
					switch (e)
					{
						case OperationCanceledException when cts.IsCancellationRequested:
							return AppTaskResult.Canceled.Instance;
						case OperationCanceledException:
							throw;
						default:
							return new AppTaskResult.Error(e);
					}
				}
			}
		}

		private ExecutingAppTask(QueuedTask queuedTask, Task<AppTaskResult> runningTask, CancellationTokenSource cts,
			CancellationTokenSource linkedCts)
		{
			QueuedTask = queuedTask;
			RunningTask = runningTask;
			_cts = cts;
			_linkedCts = linkedCts;
		}
	}

	private sealed class ProcessingContext
	{
		private readonly ISubject<Event> _eventSubject;
		private readonly ILogger _logger;
		private readonly HashSet<AppTask> _requestedTasks = [];
		private readonly HashSet<AppTask> _skippedTasks = [];
		private Queue<QueuedTask> _queue;

		public ExecutingAppTask? ProcessingAppTask { get; private set; }

		/// <summary>The processing task plus the queued tasks that are not skipped; skipped tasks are always queued.</summary>
		public int VisibleTaskCount => _queue.Count - _skippedTasks.Count + (ProcessingAppTask is null ? 0 : 1);

		public ProcessingContext(ISubject<Event> eventSubject, ILogger logger)
		{
			_queue = new Queue<QueuedTask>();
			_eventSubject = eventSubject;
			_logger = logger;
		}

		/// <summary>
		/// Starts the first queued task that is not skipped. Skipped tasks reaching the front complete without running:
		/// their dependencies precede them in the queue, so they are done by then.
		/// </summary>
		public bool TryStartNewAppTask(
			Func<AppTask, CancellationToken, Task> processingFunc, [NotNullWhen(true)] out ExecutingAppTask? processingAppTask,
			CancellationToken cancellationToken)
		{
			while (_queue.TryDequeue(out var queuedTask))
			{
				if (_skippedTasks.Remove(queuedTask.AppTask))
				{
					Forget(queuedTask.AppTask);
					queuedTask.Tcs.TrySetResult();
					continue;
				}

				ProcessingAppTask = processingAppTask = ExecutingAppTask.Create(queuedTask, processingFunc, cancellationToken);
				return true;
			}

			processingAppTask = null;
			return false;
		}

		/// <summary>
		/// Hides every queued task that reports itself <see cref="AppTask.IsUnneeded"/>. It stays in the queue, unseen,
		/// so its dependents keep waiting for its dependencies and cancelling one of those still removes them.
		/// </summary>
		public void SkipUnneeded()
		{
			var index = 0;
			foreach (var queuedTask in _queue)
			{
				var appTask = queuedTask.AppTask;
				if (_skippedTasks.Contains(appTask))
				{
					continue;
				}

				if (appTask.IsUnneeded?.Invoke() == true)
				{
					_skippedTasks.Add(appTask);
					_logger.LogInformation("App task {AppTaskId} is not needed, skipping it", appTask.Id);
					_eventSubject.OnNext(new Event.TaskRemoved(appTask, index));
				}

				index++;
			}
		}

		public bool Contains(AppTask appTask) =>
			ProcessingAppTask?.QueuedTask.AppTask == appTask || _queue.Any(x => x.AppTask == appTask);

		public bool Enqueue(QueuedTask queuedTask)
		{
			if (!Contains(queuedTask.AppTask))
			{
				_queue.Enqueue(queuedTask);
				return true;
			}

			return false;
		}

		/// <summary>Marks a task enqueued on its own rather than only as another task's dependency.</summary>
		public void MarkRequested(AppTask appTask) => _requestedTasks.Add(appTask);

		/// <summary>Forgets a task that left the queue for good.</summary>
		public void Forget(AppTask appTask) => _requestedTasks.Remove(appTask);

		public bool PushFront(QueuedTask queuedTask)
		{
			if (!Contains(queuedTask.AppTask))
			{
				_queue = new Queue<QueuedTask>(_queue.Prepend(queuedTask));
				return true;
			}

			return false;
		}

		/// <summary>
		/// Removes the task, every queued task depending on it, and every queued dependency that was not enqueued on its
		/// own and that no remaining task needs any more.
		/// </summary>
		public void Remove(string appTaskId)
		{
			var queuedTasks = _queue.ToList();
			var tasksToCheck = new Queue<string>();
			tasksToCheck.Enqueue(appTaskId);
			var tasksToRemove = new HashSet<string> { appTaskId };

			while (tasksToCheck.TryDequeue(out var checkTaskId))
			{
				foreach (var queuedTask in queuedTasks)
				{
					if (queuedTask.AppTask.Dependencies.Any(x => x.Id == checkTaskId) && !tasksToRemove.Contains(queuedTask.AppTask.Id))
					{
						tasksToCheck.Enqueue(queuedTask.AppTask.Id);
						tasksToRemove.Add(queuedTask.AppTask.Id);
					}
				}
			}

			var remainingTasks = queuedTasks.Where(x => !tasksToRemove.Contains(x.AppTask.Id)).ToList();
			int orphanIndex;
			while ((orphanIndex = remainingTasks.FindIndex(x => IsOrphaned(x.AppTask, remainingTasks))) >= 0)
			{
				tasksToRemove.Add(remainingTasks[orphanIndex].AppTask.Id);
				remainingTasks.RemoveAt(orphanIndex);
			}

			var visibleIndex = 0;
			_queue = new Queue<QueuedTask>(queuedTasks
				.Where(x =>
				{
					var index = visibleIndex;
					var skipped = _skippedTasks.Contains(x.AppTask);
					if (!skipped)
					{
						visibleIndex++;
					}

					if (!tasksToRemove.Contains(x.AppTask.Id))
					{
						return true;
					}

					_requestedTasks.Remove(x.AppTask);
					x.Tcs.TrySetResult();
					if (!_skippedTasks.Remove(x.AppTask))
					{
						_eventSubject.OnNext(new Event.TaskRemoved(x.AppTask, index));
					}

					return false;
				}));
		}

		public void ResetProcessingAppTask()
		{
			ProcessingAppTask?.Dispose();
			ProcessingAppTask = null;
		}

		private bool IsOrphaned(AppTask appTask, List<QueuedTask> remainingTasks) =>
			!_requestedTasks.Contains(appTask) &&
			!remainingTasks.Any(x => x.AppTask.Dependencies.Any(dependency => dependency.Id == appTask.Id));
	}
}