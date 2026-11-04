using Microsoft.Extensions.Logging;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class FeedAvailabilityChecker : IFeedAvailabilityChecker
{
	private static readonly TimeSpan ServerCheckTimeout = TimeSpan.FromSeconds(15);

	private static readonly DownloadInfo TestFileInfo = new()
	{
		S3Path = "Test.zip",
		Hash = null,
	};

	private readonly ILogger<FeedAvailabilityChecker> _logger;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IFeedFileSourceFactory _feedFileSourceFactory;
	private readonly TimeProvider _timeProvider;

	public FeedAvailabilityChecker(
		ILogger<FeedAvailabilityChecker> logger, IDownloadServerFallback downloadServerFallback,
		IFeedFileSourceFactory feedFileSourceFactory, TimeProvider timeProvider)
	{
		_logger = logger;
		_downloadServerFallback = downloadServerFallback;
		_feedFileSourceFactory = feedFileSourceFactory;
		_timeProvider = timeProvider;
	}

	public async Task<bool> CheckFeed()
	{
		try
		{
			return await _downloadServerFallback.Execute(UrlKeys.Feed, CheckServer, CancellationToken.None);
		}
		catch (DownloadServersUnavailableException e)
		{
			_logger.LogWarning(e, "Download servers are unavailable");
			return false;
		}
	}

	private async Task<bool> CheckServer(Uri feedBaseUri, CancellationToken cancellationToken)
	{
		// A server that does not finish in time counts as failed: the cancellation is not the caller's.
		using var timeoutCts = new CancellationTokenSource(ServerCheckTimeout, _timeProvider);
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
		await using var testFileStream = await _feedFileSourceFactory.Create(TestFileInfo, feedBaseUri)
			.OpenStream(null, null, linkedCts.Token);
		await testFileStream.CopyTo(Stream.Null, linkedCts.Token);
		return true;
	}
}
