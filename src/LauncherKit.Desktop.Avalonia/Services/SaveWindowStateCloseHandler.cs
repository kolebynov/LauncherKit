using Avalonia.Controls;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Avalonia.Services;

internal sealed class SaveWindowStateCloseHandler(WindowStateService windowStateService) : IAppCloseHandler
{
	public async ValueTask<IAppCloseHandler.HandleResult> Handle(Window mainWindow)
	{
		var state = mainWindow.WindowState switch
		{
			WindowState.Minimized => AppWindowState.Minimized,
			WindowState.Maximized => AppWindowState.Maximized,
			_ => AppWindowState.Normal,
		};
		await windowStateService.Save(new AppWindowSnapshot(state, mainWindow.Width, mainWindow.Height,
			mainWindow.Position.X, mainWindow.Position.Y));
		return IAppCloseHandler.HandleResult.CanClose.Instance;
	}
}
