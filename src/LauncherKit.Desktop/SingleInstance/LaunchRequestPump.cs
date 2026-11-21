using LauncherKit.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Drains the <see cref="LaunchRequestQueue"/> on the UI thread once started (when the main window is shown), one context
/// at a time. Each context goes, in registration order, to every <see cref="ILaunchHandler"/> of the scope current when
/// its handling starts (the kit's <see cref="MainWindowActivationLaunchHandler"/> first); each handler is awaited before
/// the next one and the next context waits for all of them. Without registered handlers contexts are dropped; a failing
/// handler is logged and the remaining handlers and following contexts still run. Disposing it (also by a handler) cancels
/// the token passed to the running handler and stops before the next handler call.
/// </summary>
internal sealed class LaunchRequestPump(
	ILaunchRequestQueue queue,
	IUiDispatcher dispatcher,
	LauncherKitDesktopOptions options,
	ILogger<LaunchRequestPump> logger) : IDisposable
{
	private readonly CancellationTokenSource _disposeCts = new();
	private IDisposable? _subscription;
	private bool _draining;
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
		if (_disposed) return;
		_disposed = true;
		_subscription?.Dispose();
		_disposeCts.Cancel();
		_disposeCts.Dispose();
	}

	// A drain already awaiting a handler picks up the newly enqueued contexts itself.
	private void Drain()
	{
		if (_draining || _disposed) return;
		_ = DrainAsync();
	}

	// The context stays out of every log template; handlers are consumer code, so one failure must not stop the others.
	// Every exception is caught here, so the discarded task never faults.
	private async Task DrainAsync()
	{
		_draining = true;
		try
		{
			var cancellationToken = _disposeCts.Token;
			while (!_disposed && queue.TryDequeue(out var context))
			{
				foreach (var handler in options.GetCurrentServices().GetServices<ILaunchHandler>().ToArray())
				{
					if (_disposed) return;
					try
					{
						await handler.Handle(context, cancellationToken);
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						return;
					}
					catch (Exception e)
					{
						logger.LogError(e, "Failed to handle a launch");
					}
				}
			}
		}
		finally
		{
			_draining = false;
		}
	}
}
