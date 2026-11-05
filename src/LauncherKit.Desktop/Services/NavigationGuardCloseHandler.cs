using Avalonia.Controls;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Services;

/// <summary>Keeps the main window open when its current page, an <see cref="INavigationGuard"/>, refuses to be left.</summary>
internal sealed class NavigationGuardCloseHandler : IAppCloseHandler
{
	public async ValueTask<IAppCloseHandler.HandleResult> Handle(Window mainWindow) =>
		mainWindow is not IMainView mainView || await NavigationGuard.CanLeave(mainView)
			? IAppCloseHandler.HandleResult.CanClose.Instance
			: IAppCloseHandler.HandleResult.CannotClose.Instance;
}
