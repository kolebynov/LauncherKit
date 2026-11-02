using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Utils;

public sealed class FeedUtils : IFeedUtils
{
	private readonly IUrlResolver _urlResolver;

	public FeedUtils(IUrlResolver urlResolver)
	{
		_urlResolver = urlResolver;
	}

	public Uri GetUri(string filePath) => new(_urlResolver.GetUri(UrlKeys.Feed), filePath);

	public Uri GetUri(string filePath, DownloadServer server) =>
		new(_urlResolver.GetUri(UrlKeys.Feed, server), filePath);
}
