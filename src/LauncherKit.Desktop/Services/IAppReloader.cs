namespace LauncherKit.Desktop.Services;

public interface IAppReloader
{
	void ReloadApp(AppReloadMode mode);
}

public enum AppReloadMode
{
	Configuration,
	Language,
}
