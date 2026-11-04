namespace LauncherKit.Desktop.Services;

public interface IAppReloader
{
	/// <summary>Rebuilds the views, e.g. to show texts in a newly applied language.</summary>
	void ReloadViews();
}
