using Avalonia.Controls;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// The kit's launch handler, registered first by
/// <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLauncherKitDesktop"/> so it runs before the consumer's:
/// brings the main window to the front for each later launch, restoring it when minimized.
/// <see cref="LauncherKitApp"/> guarantees the <see cref="IMainView"/> is the main window.
/// </summary>
internal sealed class MainWindowActivationLaunchHandler(IMainView mainView) : ILaunchHandler
{
	public void Handle(LaunchContext context)
	{
		if (context.IsFirstLaunch) return;
		var window = (Window)mainView;
		if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
		window.Activate();
	}
}
