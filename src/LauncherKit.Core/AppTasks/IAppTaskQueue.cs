using System.ComponentModel;
using R3;

namespace LauncherKit.Core.AppTasks;

public interface IAppTaskQueue : IAsyncDisposable, INotifyPropertyChanged
{
	Observable<AppTaskQueue.Event> Events { get; }

	/// <summary>
	/// Number of tasks visible in the queue: the running one, failed ones waiting for resume or cancellation, and queued
	/// ones that are not skipped as unneeded. <see cref="INotifyPropertyChanged.PropertyChanged"/> is raised on the
	/// queue's processing thread.
	/// </summary>
	int TaskCount { get; }

	ValueTask<AppTaskEnqueueResult> Enqueue(AppTask task, CancellationToken cancellationToken);

	Task EnqueueAndWaitForCompletion(AppTask task, CancellationToken cancellationToken);

	void Resume();

	Task CancelAppTask(string appTaskId, CancellationToken cancellationToken);
}
