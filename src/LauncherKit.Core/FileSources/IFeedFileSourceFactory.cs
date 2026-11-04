using LauncherKit.Core.Feed;

namespace LauncherKit.Core.FileSources;

public interface IFeedFileSourceFactory
{
	/// <summary>A source for <paramref name="downloadInfo"/> on the feed server whose base URL is <paramref name="feedBaseUri"/>.</summary>
	IFileSource Create(DownloadInfo downloadInfo, Uri feedBaseUri);
}
