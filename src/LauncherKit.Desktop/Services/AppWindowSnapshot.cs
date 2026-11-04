namespace LauncherKit.Desktop.Services;

public enum AppWindowState
{
	Normal,
	Minimized,
	Maximized,
}

public sealed record AppWindowSnapshot(AppWindowState State, double Width, double Height, int? PositionX, int? PositionY);
