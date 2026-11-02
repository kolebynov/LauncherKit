using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Polly;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.FileSources;

internal sealed class HttpFileSource : IFileSource
{
	private static readonly PolicyBuilder<int> RetryOnFailurePolicyBuilder =
		Policy<int>.Handle<IOException>().Or<HttpRequestException>();

	private readonly HttpClient _httpClient;
	private readonly ILoggerFactory _loggerFactory;
	private readonly TimeProvider _timeProvider;
	private readonly IDownloadRateLimiter _rateLimiter;
	private readonly Uri _fileUri;
	private readonly Sha256Hash? _hash;
	private long? _fileSize;
	private bool? _isOffsetSupported;

	public HttpFileSource(
		HttpClient httpClient, ILoggerFactory loggerFactory, TimeProvider timeProvider,
		IDownloadRateLimiter rateLimiter, Uri fileUri, Sha256Hash? hash)
	{
		_httpClient = httpClient;
		_loggerFactory = loggerFactory;
		_timeProvider = timeProvider;
		_rateLimiter = rateLimiter;
		_fileUri = fileUri;
		_hash = hash;
	}

	public async ValueTask<long> GetFileSize(CancellationToken cancellationToken)
	{
		if (_fileSize.HasValue)
		{
			return _fileSize.Value;
		}

		using var request = new HttpRequestMessage(HttpMethod.Head, _fileUri);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();

		return (_fileSize = response.Content.Headers.ContentLength!.Value).Value;
	}

	public async ValueTask<bool> IsOffsetSupported(CancellationToken cancellationToken)
	{
		if (_isOffsetSupported.HasValue)
		{
			return _isOffsetSupported.Value;
		}

		using var request = new HttpRequestMessage(HttpMethod.Get, _fileUri);
		request.Headers.Range = new RangeHeaderValue(1, null);
		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		return (_isOffsetSupported = response.StatusCode == HttpStatusCode.PartialContent).Value;
	}

	public ValueTask<Sha256Hash?> GetHash(CancellationToken cancellationToken) => ValueTask.FromResult(_hash);

	// The throttle is outermost, so Position keeps flowing through the retry decorator's cross-stream
	// accounting and a retried read re-reads only the already-truncated slice.
	public async ValueTask<IFileStream> OpenStream(long? fromInclusive, long? toInclusive, CancellationToken cancellationToken)
	{
#pragma warning disable CA2000
		return new ThrottledFileStream(
			new RetryOnFailureFileStream(
				await OpenInnerStream(fromInclusive, toInclusive, cancellationToken),
				(offset, ct) => OpenInnerStream(
					fromInclusive == null && offset == 0 ? null : fromInclusive.GetValueOrDefault() + offset,
					toInclusive, ct),
				_loggerFactory.CreateLogger<RetryOnFailureFileStream>(), RetryOnFailurePolicyBuilder, _timeProvider),
			_rateLimiter);
#pragma warning restore CA2000
	}

	private async Task<IFileStream> OpenInnerStream(long? from, long? to, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, _fileUri)
		{
			// Parts are downloaded concurrently, and each HTTP/1.1 request gets its own connection with its
			// own congestion window. HTTP/2 would multiplex all of them onto a single connection instead.
			Version = HttpVersion.Version11,
			VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
		};
		var offsetSet = from != null || to != null;
		if (offsetSet)
		{
			request.Headers.Range = new RangeHeaderValue(from, to);
		}

		var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (offsetSet && response.StatusCode != HttpStatusCode.PartialContent)
		{
			throw new InvalidOperationException("Server does not support range requests");
		}

		var httpResponseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
		return new HttpFileStream(httpResponseStream);
	}
}