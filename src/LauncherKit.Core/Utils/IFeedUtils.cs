using LauncherKit.Core.Services;

namespace LauncherKit.Core.Utils;

public interface IFeedUtils
{
	/// <summary>Resolves a feed-relative path against the preferred server.</summary>
	Uri GetUri(string filePath);

	Uri GetUri(string filePath, DownloadServer server);
}
