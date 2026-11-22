using LauncherKit.Core.Initialization;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Drains the <see cref="LaunchRequestQueue"/> on the UI thread once the main window has opened, one context at a time.
/// One pump exists per application scope: started as an <see cref="IScopeInitializer"/> for every scope (also without a
/// Skyrim directory) and disposed with its scope. Each context goes, in registration order, to every
/// <see cref="ILaunchHandler"/> of the pump's own scope (the kit's <see cref="MainWindowActivationLaunchHandler"/> first);
/// each handler is awaited before the next one and the next context waits for all of them. Without registered handlers
/// contexts are dropped; a failing handler is logged and the remaining handlers and following contexts still run; failing
/// to create a context's handlers is logged and skips that context.
/// Disposing it cancels the token passed to the running handler and stops before the next handler call.
/// </summary>
internal sealed class LaunchRequestPump(
	ILaunchRequestQueue queue,
	IUiDispatcher dispatcher, IAvaloniaApplicationLifetime applicationLifetime,
	ILogger<LaunchRequestPump> logger,
	IServiceProvider serviceProvider) : IScopeInitializer, IDisposable
{
	private readonly CancellationTokenSource _disposeCts = new();
	private IDisposable? _subscription;
	private bool _draining;
	private bool _disposed;

	public void Initialize()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_subscription = queue.Enqueued.Subscribe(_ => dispatcher.Post(Drain));
		dispatcher.Post(Drain);
	}

	// Cancellation callbacks are consumer code (e.g. closing a dialog); their failure must not stop the shutdown.
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_subscription?.Dispose();
		try
		{
			_disposeCts.Cancel();
		}
		catch (AggregateException e)
		{
			logger.LogError(e, "Failed to cancel the running launch handler");
		}
		finally
		{
			_disposeCts.Dispose();
		}
	}

	private void Drain()
	{
		_ = DrainAsync();
	}

	// The context stays out of every log template; handlers are consumer code, so one failure must not stop the others.
	// Resolving the handlers is guarded too: a constructor failure skips that context's handlers.
	private async Task DrainAsync()
	{
		if (_draining || _disposed) return;
		await applicationLifetime.MainWindowOpened.WaitForCancel(_disposeCts.Token)
			.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
		if (_draining || _disposed) return;

		_draining = true;
		try
		{
			var cancellationToken = _disposeCts.Token;
			while (!_disposed && queue.TryDequeue(out var context))
			{
				ILaunchHandler[] handlers;
				try
				{
					handlers = serviceProvider.GetServices<ILaunchHandler>().ToArray();
				}
				catch (Exception e)
				{
					logger.LogError(e, "Failed to create the launch handlers");
					continue;
				}

				foreach (var handler in handlers)
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
