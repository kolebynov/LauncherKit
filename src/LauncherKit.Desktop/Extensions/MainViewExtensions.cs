using Avalonia.Controls;

namespace LauncherKit.Desktop.Extensions;

public static class MainViewExtensions
{
	extension(IMainView mainView)
	{
		public Window AsWindow() => mainView as Window ??
		                            throw new InvalidOperationException("IMainView must derive from Avalonia.Controls.Window");
	}
}