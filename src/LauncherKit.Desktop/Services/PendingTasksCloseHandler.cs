using Avalonia.Controls;
using LauncherKit.Core.AppTasks;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Localization;
using R3;

namespace LauncherKit.Desktop.Services;

/// <summary>
/// Asks to confirm closing while tasks are unfinished; the question closes itself and allows the close once the queue
/// empties.
/// </summary>
internal sealed class PendingTasksCloseHandler(IAppTaskQueue appTaskQueue, IDialogService dialogs,
	IStringLocalizer<DesktopStrings> strings, IUiDispatcher dispatcher) : IAppCloseHandler
{
	public async ValueTask<IAppCloseHandler.HandleResult> Handle(Window mainWindow)
	{
		if (appTaskQueue.TaskCount == 0)
		{
			return IAppCloseHandler.HandleResult.CanClose.Instance;
		}

		using var tasksCompletedCts = new CancellationTokenSource();
		using var tasksCompletedSubscription = appTaskQueue.ObservePropertyChanged(x => x.TaskCount)
			.Where(x => x == 0)
			.Take(1)
			.ObserveOnUi(dispatcher)
			.Subscribe(_ => tasksCompletedCts.Cancel());
		var confirmed = await dialogs.ShowYesNoConfirmation(strings[DesktopStrings.PendingTasksCloseTitle],
			strings[DesktopStrings.PendingTasksCloseMessage], defaultToNo: true, tasksCompletedCts.Token);

		return confirmed || tasksCompletedCts.IsCancellationRequested
			? IAppCloseHandler.HandleResult.CanClose.Instance
			: IAppCloseHandler.HandleResult.CannotClose.Instance;
	}
}
