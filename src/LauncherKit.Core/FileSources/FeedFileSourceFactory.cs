using Microsoft.Extensions.Logging;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Services;
using LauncherKit.Core.Utils;

namespace LauncherKit.Core.FileSources;

internal sealed class FeedFileSourceFactory : IFeedFileSourceFactory
{
	private readonly IFeedUtils _feedUtils;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILoggerFactory _loggerFactory;
	private readonly TimeProvider _timeProvider;
	private readonly IDownloadRateLimiter _rateLimiter;

	public FeedFileSourceFactory(
		IFeedUtils feedUtils, IHttpClientFactory httpClientFactory,
		ILoggerFactory loggerFactory, TimeProvider timeProvider, IDownloadRateLimiter rateLimiter)
	{
		_feedUtils = feedUtils;
		_httpClientFactory = httpClientFactory;
		_loggerFactory = loggerFactory;
		_timeProvider = timeProvider;
		_rateLimiter = rateLimiter;
	}

	public IFileSource Create(DownloadInfo downloadInfo, DownloadServer server) =>
		new HttpFileSource(
			_httpClientFactory.CreateClient(AppConstants.S3HttpClient), _loggerFactory, _timeProvider,
			_rateLimiter, _feedUtils.GetUri(downloadInfo.S3Path, server), downloadInfo.Hash);
}
