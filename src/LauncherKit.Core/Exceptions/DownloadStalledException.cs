namespace LauncherKit.Core.Exceptions;

/// <summary>A download received no data for <see cref="Timeout"/>.</summary>
#pragma warning disable CA1032
public sealed class DownloadStalledException : TimeoutException
#pragma warning restore CA1032
{
	public TimeSpan Timeout { get; }

	public DownloadStalledException(TimeSpan timeout)
		: base($"No data was received for {timeout}")
	{
		Timeout = timeout;
	}
}
