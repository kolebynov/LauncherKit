using LauncherKit.Desktop.Interop;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>The message every relaunch sends first: the running launcher shows its main window in front.</summary>
internal sealed record BringToFrontMessage
{
	/// <summary>Kit-owned kind; registering another handler for it throws.</summary>
	public const string Kind = "LauncherKit.BringToFront";
}

/// <summary>Restores the main window if minimized and activates it; runs on the UI thread like every message handler.</summary>
internal sealed class BringToFrontMessageHandler(LauncherKitDesktopOptions options) : ILauncherMessageHandler<BringToFrontMessage>
{
	public void Handle(BringToFrontMessage message)
	{
		if (options.GetMainWindow() is not { } window) return;
		if (window.TryGetPlatformHandle() is { } handle) SingleInstanceNativeMethods.RestoreIfMinimized(handle.Handle);
		window.Activate();
	}
}
