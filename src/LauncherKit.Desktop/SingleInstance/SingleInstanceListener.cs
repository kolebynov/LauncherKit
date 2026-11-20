using System.IO.Pipes;
using Serilog;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// The owner's pipe server: admits each relaunch's frames into the <see cref="LaunchRequestQueue"/> until the relaunch
/// closes, one connection at a time, each bounded by the exchange timeout. Nothing is answered; invalid frames and frames
/// the queue does not admit are dropped without stopping it. Failures go to the static Serilog logger, which the
/// application configures later.
/// </summary>
internal sealed class SingleInstanceListener : IDisposable
{
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _loop;

	public SingleInstanceListener(string pipeName, LaunchRequestQueue queue)
		: this(pipeName, queue, () => SingleInstanceProtocol.ExchangeTimeout, null)
	{
	}

	/// <summary>
	/// Tests choose each connection's timeout through <paramref name="exchangeTimeout"/> to exercise stalled relaunches,
	/// and observe through <paramref name="disconnected"/> when a connection has been disconnected for reuse.
	/// </summary>
	internal SingleInstanceListener(string pipeName, LaunchRequestQueue queue, Func<TimeSpan> exchangeTimeout,
		Action? disconnected) =>
		_loop = Task.Run(() => Listen(pipeName, queue, exchangeTimeout, disconnected, _stop.Token));

	public void Dispose()
	{
		_stop.Cancel();
		// The loop catches its own failures and ends on cancellation; waiting keeps the pipe until ownership is released.
		_loop.GetAwaiter().GetResult();
		_stop.Dispose();
	}

	private static async Task Listen(string pipeName, LaunchRequestQueue queue, Func<TimeSpan> exchangeTimeout,
		Action? disconnected,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await using var server = Create(pipeName);
			if (server is null) return;

			// The instance is reused after every connection: recreating it fails while a stalled client holds its handle.
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					await Serve(server, queue, exchangeTimeout, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					// Shutdown during an accepted connection: best-effort disconnect so the client sees the end
					// promptly rather than when disposal closes the instance. Cancelled while waiting for a client,
					// nothing is connected.
					if (server.IsConnected) TryDisconnectOnShutdown(server);
					return;
				}
				catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException)
				{
					Log.Warning("Dropped the rest of a launcher relaunch connection: {FailureType}", e.GetType().Name);
				}

				try
				{
					server.Disconnect();
					disconnected?.Invoke();
				}
				catch (Exception e) when (e is IOException or InvalidOperationException)
				{
					// This instance cannot be reused (for example, the accept itself failed); it is recreated instead.
					Log.Warning("Recreating the launcher relaunch pipe: {FailureType}", e.GetType().Name);
					break;
				}
			}
		}
	}

	private static void TryDisconnectOnShutdown(NamedPipeServerStream server)
	{
		try
		{
			server.Disconnect();
		}
		catch (Exception e) when (e is IOException or InvalidOperationException)
		{
			Log.Warning("Could not disconnect a launcher relaunch connection on shutdown: {FailureType}", e.GetType().Name);
		}
	}

	/// <summary>Null when another process holds the pipe name; relaunches then reach nobody and exit.</summary>
	private static NamedPipeServerStream? Create(string pipeName)
	{
		try
		{
			return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
				PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			Log.Error(e, "Cannot listen for launcher relaunches");
			return null;
		}
	}

	private static async Task Serve(NamedPipeServerStream server, LaunchRequestQueue queue, Func<TimeSpan> exchangeTimeout,
		CancellationToken cancellationToken)
	{
		// A client that disconnects before the accept completes (ERROR_NO_DATA) fails only its own request.
		await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(exchangeTimeout());
		while (await SingleInstanceProtocol.ReadFrame(server, timeout.Token).ConfigureAwait(false)
			is not SingleInstanceProtocol.Frame.End and var frame)
		{
			// Frame contents are untrusted text from another process, so they stay out of the logs.
			if (frame is not SingleInstanceProtocol.Frame.Request(var request))
				Log.Warning("Dropped an invalid launcher relaunch request");
			else if (!queue.TryEnqueue(request))
				Log.Warning("Dropped a launcher relaunch request: the queue is full or closed");
		}
	}
}
