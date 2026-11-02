using Microsoft.Extensions.Logging;

namespace LauncherKit.Bootstrapper;

public sealed class InterprocessLockFactory : IInterprocessLockFactory
{
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

	private readonly TimeProvider _timeProvider;
	private readonly ILogger<InterprocessLockFactory> _logger;

	public InterprocessLockFactory(TimeProvider timeProvider, ILogger<InterprocessLockFactory> logger)
	{
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public async Task<IDisposable?> TryAcquire(string name, CancellationToken cancellationToken)
	{
		var lockFilePath = Path.Combine(name, ".lock");

		while (true)
		{
			try
			{
				_logger.LogInformation("Trying to acquire interprocess lock");
				var lockFile = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 0,
					FileOptions.DeleteOnClose);
				_logger.LogInformation("Acquired interprocess lock");
				return lockFile;
			}
			catch (IOException)
			{
				_logger.LogWarning("Acquire interprocess lock failed, retrying in {RetryDelay}", RetryDelay);
			}

			try
			{
				await Task.Delay(RetryDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return null;
			}
		}
	}
}
