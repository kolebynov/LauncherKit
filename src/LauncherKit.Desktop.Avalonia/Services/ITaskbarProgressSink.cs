using LauncherKit.Desktop.Avalonia.Interop;
using LauncherKit.Desktop.Avalonia.Services;

namespace LauncherKit.Desktop.Avalonia.Services;

public interface ITaskbarProgressSink
{
	void SetProgress(TaskbarProgressBarState state, ulong completed, ulong total);
}
