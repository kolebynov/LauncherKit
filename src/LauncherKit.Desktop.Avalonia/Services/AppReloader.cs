using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Avalonia.Services;

internal sealed class AppReloader : IAppReloader
{
	private readonly Action _reloadConfiguration;
	private readonly Action _reloadViews;

	public AppReloader(Action reloadConfiguration, Action reloadViews)
	{
		_reloadConfiguration = reloadConfiguration;
		_reloadViews = reloadViews;
	}

	public void ReloadApp(AppReloadMode mode)
	{
		switch (mode)
		{
			case AppReloadMode.Configuration:
				_reloadConfiguration();
				break;
			case AppReloadMode.Language:
				_reloadViews();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
		}
	}
}
