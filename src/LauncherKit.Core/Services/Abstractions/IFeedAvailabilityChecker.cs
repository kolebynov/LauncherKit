namespace LauncherKit.Core.Services.Abstractions;

public interface IFeedAvailabilityChecker
{
	/// <summary>
	/// Downloads a test file through <see cref="IDownloadServerFallback"/>, so the preferred server is tried first
	/// and the other one becomes preferred when only it works.
	/// </summary>
	/// <returns><see langword="true"/> when at least one download server works.</returns>
	Task<bool> CheckFeed();
}
