using System.IO.Pipes;
using LauncherKit.Desktop.Interop;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Decides at startup whether this process owns the launcher: it does when it creates and owns the session-local mutex.
/// Otherwise it sends its frames to the owner, best effort and one-way, and never becomes the owner.
/// </summary>
internal static class SingleInstanceElection
{
	private const int ConnectTimeoutMilliseconds = 2000;

	public union Result(Result.Owner, Result.Secondary)
	{
		/// <summary>This process owns the launcher until it releases and disposes <paramref name="Mutex"/> on this thread.</summary>
		public sealed record Owner(Mutex Mutex);

		/// <summary>Another launcher owns it; this process exits.</summary>
		public sealed record Secondary;
	}

	/// <summary>
	/// Must run on the thread that later releases an owned mutex. Only when another process owns the launcher does it send
	/// the <see cref="BringToFrontMessage"/> followed by the messages <paramref name="sendMessages"/> sends.
	/// </summary>
	public static Result Run(SingleInstanceNames names, Action<ILauncherMessageSender>? sendMessages)
	{
		Mutex? mutex = new(false, names.Mutex, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true });
		try
		{
			if (TryAcquire(mutex))
			{
				var owned = mutex;
				mutex = null;
				return new Result.Owner(owned);
			}

			var sender = new LauncherMessageSender();
			sender.Send(BringToFrontMessage.Kind, BringToFrontMessage.Instance, SingleInstanceJsonContext.Default.BringToFrontMessage);
			try
			{
				sendMessages?.Invoke(sender);
			}
			// Consumer code (or an oversized message): what it sent so far still goes, and the relaunch still exits normally.
			catch (Exception)
			{
			}

			// On the thread pool, so blocking here cannot deadlock under the caller's single-threaded context.
			Task.Run(() => Send(names.Pipe, sender.Frames)).GetAwaiter().GetResult();
			return Result.Secondary.Instance;
		}
		finally
		{
			mutex?.Dispose();
		}
	}

	private static bool TryAcquire(Mutex mutex)
	{
		try
		{
			return mutex.WaitOne(0);
		}
		catch (AbandonedMutexException)
		{
			// The previous owner died without releasing it; this thread owns it now.
			return true;
		}
	}

	/// <summary>
	/// Writes <paramref name="frames"/>, waits until the owner has read them and closes; an owner that is unreachable
	/// (exiting, another elevation level, busy past the timeouts) simply misses them.
	/// </summary>
	private static async Task Send(string pipeName, IReadOnlyList<byte[]> frames)
	{
		try
		{
			await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
				PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			await client.ConnectAsync(ConnectTimeoutMilliseconds).ConfigureAwait(false);
			SingleInstanceNativeMethods.AllowPipeServerForeground(client.SafePipeHandle);
			using var timeout = new CancellationTokenSource(SingleInstanceProtocol.ExchangeTimeout);
			foreach (var frame in frames) await SingleInstanceProtocol.WriteFrame(client, frame, timeout.Token).ConfigureAwait(false);
			await client.FlushAsync(timeout.Token).ConfigureAwait(false);
			// Closing before the owner reads could make it drop the unread frames (ERROR_NO_DATA on accept).
			await Task.Run(client.WaitForPipeDrain, CancellationToken.None).WaitAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
		{
		}
	}
}
