namespace LauncherKit.Core.FileSources;

public interface IDownloadRateLimiter
{
	/// <summary>
	/// Waits until the limiter can grant at least part of <paramref name="maxBytes"/> and returns the
	/// granted amount. Returns <paramref name="maxBytes"/> immediately when no limit is configured.
	/// </summary>
	ValueTask<int> AcquireAtMost(int maxBytes, CancellationToken cancellationToken);

	/// <summary>Returns bytes that were granted but not consumed.</summary>
	void Refund(int bytes);
}
