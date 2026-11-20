using LauncherKit.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using R3;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Drains the <see cref="LaunchRequestQueue"/> on the UI thread once started (when the main window is shown): each
/// message goes to the <see cref="LauncherMessageProcessor"/> of the scope current at that moment, so scope replacements
/// (made in one UI turn) never see a disposed scope. Stops when disposed.
/// </summary>
internal sealed class LaunchRequestPump(LaunchRequestQueue queue, IUiDispatcher dispatcher, LauncherKitDesktopOptions options)
	: IDisposable
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

	private void Drain()
	{
		while (!_disposed && queue.TryDequeue(out var request))
			options.GetCurrentServices().GetRequiredService<LauncherMessageProcessor>().Process(request.Kind, request.Payload);
	}
}
