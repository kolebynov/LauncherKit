namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Handles every launch of the launcher, registered with
/// <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLaunchHandler{THandler}"/>: the launch that started it
/// (<see cref="LaunchContext.IsFirstLaunch"/>) and each later launch forwarded to it, in admission order. Every registered
/// handler gets each launch, in registration order; an exception is logged and the next handler still runs. Called on the
/// UI thread after the main window has opened, resolved from the application scope current when the launch's handling
/// starts. The kit awaits each handler before calling the next one and handles one launch at a time, so a later launch's
/// handlers start only after the earlier launch's handlers have completed (a long-running handler, e.g. one awaiting a
/// dialog, delays later launches). The kit registers its own handler first, which restores and activates the main window
/// for a later launch, so handlers need not do that.
/// </summary>
public interface ILaunchHandler
{
	/// <param name="context">The launch.</param>
	/// <param name="cancellationToken">Cancelled when the launcher shuts down (the kit's services are disposed).</param>
	ValueTask Handle(LaunchContext context, CancellationToken cancellationToken);
}
