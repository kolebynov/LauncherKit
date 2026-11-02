using Avalonia.Threading;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Avalonia.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
