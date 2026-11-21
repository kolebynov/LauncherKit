using LauncherKit.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Drains the <see cref="LaunchRequestQueue"/> on the UI thread once started (when the main window is shown). Each context
/// goes, in registration order, to every <see cref="ILaunchHandler"/> of the scope current at that moment (the kit's
/// <see cref="MainWindowActivationLaunchHandler"/> first), so scope replacements (made in one UI turn) never see a disposed
/// scope. Without registered handlers contexts are dropped; a failing handler is logged and the remaining handlers and
/// following contexts still run. Stops when disposed, also by a handler, before the next handler call.
/// </summary>
internal sealed class LaunchRequestPump(
	ILaunchRequestQueue queue,
	IUiDispatcher dispatcher,
	LauncherKitDesktopOptions options,
	ILogger<LaunchRequestPump> logger) : IDisposable
{
	private IDisposable? _subscription;
	private bool _disposed;

	/// <summary>Drains the backlog and every later request.</summary>
	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_subscription is not null) throw new InvalidOperationException("The launch request pump is already started.");
		_subscription = queue.Enqueued.Subscribe(_ => dispatcher.Post(Drain));
		dispatcher.Post(Drain);
	}

	public void Dispose()
	{
		_disposed = true;
		_subscription?.Dispose();
	}

	// The context stays out of every log template; handlers are consumer code, so one failure must not stop the others.
	private void Drain()
	{
		while (!_disposed && queue.TryDequeue(out var context))
		{
			foreach (var handler in options.GetCurrentServices().GetServices<ILaunchHandler>())
			{
				if (_disposed) return;
				try
				{
					handler.Handle(context);
				}
				catch (Exception e)
				{
					logger.LogError(e, "Failed to handle a launch");
				}
			}
		}
	}
}
