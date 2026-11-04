namespace LauncherKit.Core.Exceptions;

/// <summary>
/// A download failed on every server of its URL key: the main server and the proxy, or the main server alone when the
/// key has no proxy.
/// </summary>
#pragma warning disable CA1032
public sealed class DownloadServersUnavailableException : Exception
#pragma warning restore CA1032
{
	public Exception MainServerException { get; }

	/// <summary>The proxy's failure; <see langword="null"/> when the URL key has no proxy.</summary>
	public Exception? ProxyServerException { get; }

	/// <summary>The main server failed and there is no proxy to try.</summary>
	public DownloadServersUnavailableException(Exception mainServerException)
		: base($"Download failed on the main server, which has no proxy. {mainServerException.Message}", mainServerException)
	{
		MainServerException = mainServerException;
	}

	public DownloadServersUnavailableException(Exception mainServerException, Exception proxyServerException)
		: base(
			$"Download failed on both servers. Main server: {mainServerException.Message} Proxy server: {proxyServerException.Message}",
			new AggregateException(mainServerException, proxyServerException))
	{
		MainServerException = mainServerException;
		ProxyServerException = proxyServerException;
	}
}
