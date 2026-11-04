using LauncherKit.Desktop.Interop;

namespace LauncherKit.Desktop.Services;

public interface ITaskbarProgressSink
{
	void SetProgress(TaskbarProgressBarState state, ulong completed, ulong total);
}
