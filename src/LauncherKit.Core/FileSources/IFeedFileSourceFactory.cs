using LauncherKit.Core.Feed;
using LauncherKit.Core.Services;

namespace LauncherKit.Core.FileSources;

public interface IFeedFileSourceFactory
{
	IFileSource Create(DownloadInfo downloadInfo, DownloadServer server);
}
