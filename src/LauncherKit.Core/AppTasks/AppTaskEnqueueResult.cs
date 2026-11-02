﻿namespace LauncherKit.Core.AppTasks;

public union AppTaskEnqueueResult(AppTaskEnqueueResult.NotEnqueued, AppTaskEnqueueResult.Enqueued)
{
	public sealed record NotEnqueued;

	public sealed record Enqueued(Task CompletionTask);
}