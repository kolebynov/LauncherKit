using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Tests;

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => action();
}
