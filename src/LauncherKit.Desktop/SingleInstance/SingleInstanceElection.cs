using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Decides at startup whether this process owns the launcher: it does when it creates and owns the session-local mutex.
/// Otherwise it forwards its <see cref="LaunchContext"/> to the owner, best effort and one-way, lets the owner take the
/// foreground and never becomes the owner.
/// </summary>
internal static partial class SingleInstanceElection
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
	/// Must run on the thread that later releases an owned mutex. Only when another process owns the launcher does it
	/// forward <paramref name="args"/> and <paramref name="workingDirectory"/>, even without arguments.
	/// </summary>
	public static Result Run(SingleInstanceNames names, IReadOnlyList<string> args, string workingDirectory)
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

			byte[] frame;
			try
			{
				frame = SingleInstanceProtocol.Serialize(new LaunchContext(args, workingDirectory, false));
			}
			// An oversized launch is dropped; the relaunch still exits normally.
			catch (ArgumentException)
			{
				return Result.Secondary.Instance;
			}

			// On the thread pool, so blocking here cannot deadlock under the caller's single-threaded context.
			Task.Run(() => Send(names.Pipe, frame)).GetAwaiter().GetResult();
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
	/// Lets the connected owner take the foreground, writes <paramref name="frame"/>, waits until the owner has read it and
	/// closes; an owner that is unreachable (exiting, another elevation level, busy past the timeouts) simply misses it.
	/// </summary>
	private static async Task Send(string pipeName, byte[] frame)
	{
		try
		{
			await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
				PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			await client.ConnectAsync(ConnectTimeoutMilliseconds).ConfigureAwait(false);
			AllowOwnerForeground(client.SafePipeHandle);
			using var timeout = new CancellationTokenSource(SingleInstanceProtocol.ExchangeTimeout);
			await SingleInstanceProtocol.WriteFrame(client, frame, timeout.Token).ConfigureAwait(false);
			await client.FlushAsync(timeout.Token).ConfigureAwait(false);
			// Closing before the owner reads could make it drop the unread frame (ERROR_NO_DATA on accept).
			await Task.Run(client.WaitForPipeDrain, CancellationToken.None).WaitAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
		{
		}
	}

	/// <summary>
	/// A process that may take the foreground (this relaunch, when the user started it) passes that right to the connected
	/// owner, so the owner can bring its main window to the front, which it does for every relaunch. Failures only lose the
	/// permission, never the delivery.
	/// </summary>
	private static void AllowOwnerForeground(SafePipeHandle pipe)
	{
		if (GetNamedPipeServerProcessId(pipe, out var ownerProcessId)) AllowSetForegroundWindow(ownerProcessId);
	}

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

	[LibraryImport("user32.dll", SetLastError = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool AllowSetForegroundWindow(uint processId);
}
