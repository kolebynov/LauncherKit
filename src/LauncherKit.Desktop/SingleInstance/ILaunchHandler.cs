namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Handles every launch of the launcher, registered with
/// <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLaunchHandler{THandler}"/>: the launch that started it
/// (<see cref="LaunchContext.IsFirstLaunch"/>) and each later launch forwarded to it, in admission order. Every registered
/// handler gets each launch, in registration order; an exception is logged and the next handler still runs. Called on the
/// UI thread after the main window has opened, resolved from the application scope current at that moment. The kit
/// registers its own handler first, which restores and activates the main window for a later launch, so handlers need not do that.
/// Keep it short and synchronous: start the work through the application's
/// usual services instead of awaiting it here.
/// </summary>
public interface ILaunchHandler
{
	void Handle(LaunchContext context);
}
