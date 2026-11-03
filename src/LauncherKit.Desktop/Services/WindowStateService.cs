using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.Services;

/// <summary>Restores and persists window facts without depending on a desktop toolkit.</summary>
public sealed class WindowStateService(ISettingsService settingsService)
{
	public AppWindowSnapshot Restore()
	{
		var settings = settingsService.GetSettings().Window;
		return new AppWindowSnapshot(
			settings.IsMaximized ? AppWindowState.Maximized : AppWindowState.Normal,
			settings.Width, settings.Height, settings.PositionX, settings.PositionY);
	}

	public ValueTask Save(AppWindowSnapshot window)
	{
		if (window.State == AppWindowState.Minimized)
		{
			return ValueTask.CompletedTask;
		}

		return new ValueTask(settingsService.ModifySettings(settings =>
		{
			settings.Window.IsMaximized = window.State == AppWindowState.Maximized;
			if (window.State == AppWindowState.Normal)
			{
				settings.Window.Width = window.Width;
				settings.Window.Height = window.Height;
				settings.Window.PositionX = window.PositionX;
				settings.Window.PositionY = window.PositionY;
			}

			return ValueTask.CompletedTask;
		}));
	}
}
