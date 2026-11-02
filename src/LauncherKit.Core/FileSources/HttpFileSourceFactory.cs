using Microsoft.Extensions.Logging;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.FileSources;

public sealed class HttpFileSourceFactory : IHttpFileSourceFactory
{
	private readonly HttpClient _httpClient;
	private readonly ILoggerFactory _loggerFactory;
	private readonly TimeProvider _timeProvider;
	private readonly IDownloadRateLimiter _rateLimiter;

	public HttpFileSourceFactory(
		HttpClient httpClient, ILoggerFactory loggerFactory, TimeProvider timeProvider,
		IDownloadRateLimiter rateLimiter)
	{
		_httpClient = httpClient;
		_loggerFactory = loggerFactory;
		_timeProvider = timeProvider;
		_rateLimiter = rateLimiter;
	}

	public IFileSource Create(Uri fileUri, Sha256Hash? hash) =>
		new HttpFileSource(_httpClient, _loggerFactory, _timeProvider, _rateLimiter, fileUri, hash);
}
