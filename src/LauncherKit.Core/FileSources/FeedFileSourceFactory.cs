using Microsoft.Extensions.Logging;
using LauncherKit.Core.Feed;

namespace LauncherKit.Core.FileSources;

internal sealed class FeedFileSourceFactory : IFeedFileSourceFactory
{
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILoggerFactory _loggerFactory;
	private readonly TimeProvider _timeProvider;
	private readonly IDownloadRateLimiter _rateLimiter;

	public FeedFileSourceFactory(
		IHttpClientFactory httpClientFactory,
		ILoggerFactory loggerFactory, TimeProvider timeProvider, IDownloadRateLimiter rateLimiter)
	{
		_httpClientFactory = httpClientFactory;
		_loggerFactory = loggerFactory;
		_timeProvider = timeProvider;
		_rateLimiter = rateLimiter;
	}

	public IFileSource Create(DownloadInfo downloadInfo, Uri feedBaseUri) =>
		new HttpFileSource(
			_httpClientFactory.CreateClient(AppConstants.S3HttpClient), _loggerFactory, _timeProvider,
			_rateLimiter, new Uri(feedBaseUri, downloadInfo.S3Path), downloadInfo.Hash);
}
