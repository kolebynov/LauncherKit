namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// A message a relaunch sends to the running launcher. <see cref="Kind"/> identifies the message type on the wire and
/// selects its handler; keep it stable and unique, for example <c>"MyLauncher.OpenUrl"</c>.
/// </summary>
public interface ILauncherMessage
{
	static abstract string Kind { get; }
}
