using Avalonia.Controls;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Services;

/// <summary>
/// Handles the user closing the main window: cancels the close, runs the current scope's <see cref="IAppCloseHandler"/>s
/// in registration order and closes the window when none of them keeps it open. A failing handler is logged and
/// skipped; repeated close requests while the handlers run are cancelled. Application/OS shutdown and owner closing
/// cannot be cancelled and close at once.
/// </summary>
internal sealed class MainWindowCloseHandler(LauncherKitDesktopOptions desktop, ILogger<MainWindowCloseHandler> logger)
{
	private CloseState _state = CloseState.Idle.Instance;

	public void Attach(Window window) => window.Closing += OnClosing;

	private async void OnClosing(object? sender, WindowClosingEventArgs e)
	{
		if (e.CloseReason != WindowCloseReason.WindowClosing)
		{
			return;
		}

		Func<Task> handle = _state switch
		{
			CloseState.Idle => () =>
			{
				e.Cancel = true;
				return RunHandlers((Window)sender!);
			},
			CloseState.InvokingHandlers => () =>
			{
				e.Cancel = true;
				return Task.CompletedTask;
			},
			CloseState.Closing => () => Task.CompletedTask,
		};
		await handle();
	}

	private async Task RunHandlers(Window window)
	{
		_state = CloseState.InvokingHandlers.Instance;
		foreach (var handler in desktop.GetCurrentServices().GetServices<IAppCloseHandler>())
		{
			try
			{
				if (await handler.Handle(window) is IAppCloseHandler.HandleResult.CannotClose)
				{
					_state = CloseState.Idle.Instance;
					return;
				}
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Error during app close handler invoking");
			}
		}

		_state = CloseState.Closing.Instance;
		window.Close();
	}

	private union CloseState(CloseState.Idle, CloseState.InvokingHandlers, CloseState.Closing)
	{
		public sealed record Idle;

		public sealed record InvokingHandlers;

		public sealed record Closing;
	}
}
