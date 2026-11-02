namespace LauncherKit.Core.UserSettings;

public sealed class WindowSettings
{
	public double Width { get; set; } = 1000;

	public double Height { get; set; } = 700;

	public bool IsMaximized { get; set; }

	public int? PositionX { get; set; }

	public int? PositionY { get; set; }
}