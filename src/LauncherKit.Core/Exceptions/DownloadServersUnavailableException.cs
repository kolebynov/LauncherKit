namespace LauncherKit.Core.Exceptions;

/// <summary>A download failed on both the main server and the proxy server.</summary>
#pragma warning disable CA1032
public sealed class DownloadServersUnavailableException : Exception
#pragma warning restore CA1032
{
	public Exception MainServerException { get; }

	public Exception ProxyServerException { get; }

	public DownloadServersUnavailableException(Exception mainServerException, Exception proxyServerException)
		: base(
			$"Download failed on both servers. Main server: {mainServerException.Message} Proxy server: {proxyServerException.Message}",
			new AggregateException(mainServerException, proxyServerException))
	{
		MainServerException = mainServerException;
		ProxyServerException = proxyServerException;
	}
}
