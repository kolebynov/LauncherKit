using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Avalonia.Tests;

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => action();
}
