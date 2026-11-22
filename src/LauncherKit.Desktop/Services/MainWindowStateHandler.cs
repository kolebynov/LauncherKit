using Avalonia;
using Avalonia.Controls;
using LauncherKit.Core.Initialization;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.Desktop.Services;

/// <summary>
/// Keeps the main window's state in <c>AllSettings.Window</c> through the current scope's settings: restores it when the
/// window is assigned to the lifetime (before its data context is set and it is shown) and saves it on close. A
/// minimized window saves nothing and a maximized one keeps its saved normal bounds.
/// </summary>
internal sealed class MainWindowStateHandler(IAvaloniaApplicationLifetime applicationLifetime)
	: IAppCloseHandler, IGlobalInitializer
{
	public void Initialize() => applicationLifetime.OnMainWindowAssigned(mainView => Restore(mainView.AsWindow()));

	public void Restore(Window mainWindow)
	{
		var settings = SettingsService().GetSettings().Window;
		mainWindow.Width = settings.Width;
		mainWindow.Height = settings.Height;
		if (settings.PositionX is { } x && settings.PositionY is { } y)
		{
			mainWindow.Position = new PixelPoint(x, y);
		}

		mainWindow.WindowState = settings.IsMaximized ? WindowState.Maximized : WindowState.Normal;
	}

	public async ValueTask<IAppCloseHandler.HandleResult> Handle(Window mainWindow)
	{
		if (mainWindow.WindowState != WindowState.Minimized)
		{
			await SettingsService().ModifySettings(settings =>
			{
				settings.Window.IsMaximized = mainWindow.WindowState == WindowState.Maximized;
				if (mainWindow.WindowState != WindowState.Maximized)
				{
					settings.Window.Width = mainWindow.Width;
					settings.Window.Height = mainWindow.Height;
					settings.Window.PositionX = mainWindow.Position.X;
					settings.Window.PositionY = mainWindow.Position.Y;
				}

				return ValueTask.CompletedTask;
			});
		}

		return IAppCloseHandler.HandleResult.CanClose.Instance;
	}

	private ISettingsService SettingsService() => applicationLifetime.Services.GetRequiredService<ISettingsService>();
}
