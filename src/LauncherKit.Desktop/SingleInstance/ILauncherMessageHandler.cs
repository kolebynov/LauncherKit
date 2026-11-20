namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Handles a consumer message on the UI thread in the current application scope. Keep it short: start the work through
/// the application's usual services instead of awaiting it here.
/// </summary>
public interface ILauncherMessageHandler<in TMessage>
{
	void Handle(TMessage message);
}
