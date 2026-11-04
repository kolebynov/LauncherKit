using Avalonia.Threading;

namespace LauncherKit.Desktop.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
