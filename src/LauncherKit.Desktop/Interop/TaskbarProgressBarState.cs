namespace LauncherKit.Desktop.Interop;

#pragma warning disable CA1027
public enum TaskbarProgressBarState
#pragma warning restore CA1027
{
	NoProgress    = 0,
	Indeterminate = 0x1,
	Normal        = 0x2,
	Error         = 0x4,
	Paused        = 0x8
}