using LauncherKit.Core.AppTasks;

namespace LauncherKit.Desktop.Services;

public sealed class PendingTasksCloseHandler(IAppTaskQueue appTaskQueue) : IAppCloseHandler
{
	public ValueTask<IAppCloseHandler.HandleResult> Handle(AppCloseContext context) => new(
		appTaskQueue.TaskCount == 0 || context.PendingTasksConfirmed
			? new IAppCloseHandler.HandleResult.CanClose()
			: new IAppCloseHandler.HandleResult.CannotClose());
}
