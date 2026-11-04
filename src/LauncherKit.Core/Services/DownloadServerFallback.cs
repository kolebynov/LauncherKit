using Microsoft.Extensions.Logging;
using Polly.Timeout;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

internal sealed class DownloadServerFallback : IDownloadServerFallback
{
	private readonly IUrlResolver _urlResolver;
	private readonly ILogger<DownloadServerFallback> _logger;

	public DownloadServerFallback(IUrlResolver urlResolver, ILogger<DownloadServerFallback> logger)
	{
		_urlResolver = urlResolver;
		_logger = logger;
	}

	public async Task<T> Execute<T>(
		string endpointKey, Func<Uri, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(operation);
		if (!_urlResolver.HasProxy(endpointKey))
		{
			try
			{
				return await operation(_urlResolver.GetUri(endpointKey, DownloadServer.Main), cancellationToken);
			}
			catch (Exception e) when (IsServerFailure(e, cancellationToken))
			{
				_logger.LogError(e, "Download from the {Server} server failed and {EndpointKey} has no proxy",
					DownloadServer.Main, endpointKey);
				throw new DownloadServersUnavailableException(e);
			}
		}

		var preferredServer = _urlResolver.PreferredServer;
		Exception preferredServerException;
		try
		{
			return await operation(_urlResolver.GetUri(endpointKey, preferredServer), cancellationToken);
		}
		catch (Exception e) when (IsServerFailure(e, cancellationToken))
		{
			_logger.LogWarning(e, "Download from the {Server} server failed, trying the other server", preferredServer);
			preferredServerException = e;
		}

		var otherServer = preferredServer == DownloadServer.Main ? DownloadServer.Proxy : DownloadServer.Main;
		T result;
		try
		{
			result = await operation(_urlResolver.GetUri(endpointKey, otherServer), cancellationToken);
		}
		catch (Exception e) when (IsServerFailure(e, cancellationToken))
		{
			_logger.LogError(e, "Download from the {Server} server failed too", otherServer);
			throw otherServer == DownloadServer.Proxy
				? new DownloadServersUnavailableException(preferredServerException, e)
				: new DownloadServersUnavailableException(e, preferredServerException);
		}

		SetPreferredServer(otherServer);
		return result;
	}

	/// <remarks>
	/// Only failures caused by the server or the network between: anything else, such as a full disk, would fail
	/// on the other server the same way. A cancellation the caller did not request is a timeout.
	/// </remarks>
	private static bool IsServerFailure(Exception exception, CancellationToken cancellationToken) =>
		exception is HttpRequestException or HttpIOException or TimeoutException or TimeoutRejectedException ||
		(exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

	private void SetPreferredServer(DownloadServer server)
	{
		if (_urlResolver.PreferredServer == server)
		{
			return;
		}

		_urlResolver.PreferredServer = server;
		_logger.LogInformation("The {Server} server is now preferred for downloads until the launcher exits", server);
	}
}
