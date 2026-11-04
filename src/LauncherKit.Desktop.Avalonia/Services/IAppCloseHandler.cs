using Avalonia.Controls;

namespace LauncherKit.Desktop.Avalonia.Services;

/// <summary>
/// A step of closing the main window by the user, resolved from the current scope and run in registration order;
/// the first <see cref="HandleResult.CannotClose"/> keeps the window open. Runs on the UI thread.
/// </summary>
public interface IAppCloseHandler
{
	ValueTask<HandleResult> Handle(Window mainWindow);

	public union HandleResult(HandleResult.CanClose, HandleResult.CannotClose)
	{
		public sealed record CanClose;

		public sealed record CannotClose;
	}
}
