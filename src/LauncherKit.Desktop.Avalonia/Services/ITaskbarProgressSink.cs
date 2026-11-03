using LauncherKit.Desktop.Avalonia.Interop;

namespace LauncherKit.Desktop.Avalonia.Services;

public interface ITaskbarProgressSink
{
	void SetProgress(TaskbarProgressBarState state, ulong completed, ulong total);
}
