
namespace LauncherKit.Desktop.Services;

internal sealed class AppReloader(Action reloadViews) : IAppReloader
{
	public void ReloadViews() => reloadViews();
}
