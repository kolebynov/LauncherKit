namespace LauncherKit.Desktop.Services;

/// <summary>Schedules state changes on the consumer's UI thread.</summary>
public interface IUiDispatcher
{
	void Post(Action action);
}
