using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Avalonia.Services;

internal sealed class AppReloader(Action reloadViews) : IAppReloader
{
	public void ReloadViews() => reloadViews();
}
