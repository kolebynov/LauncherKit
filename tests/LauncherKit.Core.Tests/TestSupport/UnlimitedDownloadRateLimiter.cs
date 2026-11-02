using LauncherKit.Core.FileSources;

namespace LauncherKit.Core.Tests.TestSupport;

/// <summary>Behaviorless limiter that never throttles; used where the rate limit is not under test.</summary>
internal sealed class UnlimitedDownloadRateLimiter : IDownloadRateLimiter
{
	public ValueTask<int> AcquireAtMost(int maxBytes, CancellationToken cancellationToken) =>
		ValueTask.FromResult(maxBytes);

	public void Refund(int bytes)
	{
	}
}
