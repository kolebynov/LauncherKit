namespace LauncherKit.Core.Services.Abstractions;

public interface IDownloadServerFallback
{
	/// <summary>
	/// Runs <paramref name="operation"/> with the base URL of <paramref name="endpointKey"/> on the preferred server
	/// and, when it fails because of the server (failed request, broken or stalled transfer, timeout), with the base URL
	/// on the other server. When the other server succeeds it becomes <see cref="IUrlResolver.PreferredServer"/> for the
	/// rest of the launcher run. A key without a proxy runs once, with its main URL, and leaves the preferred server as
	/// is.
	/// </summary>
	/// <param name="endpointKey">The <c>Urls</c> key (<see cref="UrlKeys"/>) whose servers to use.</param>
	/// <param name="operation">Receives the key's base URL on the server being tried.</param>
	/// <exception cref="Exceptions.DownloadServersUnavailableException">Every server of the key failed.</exception>
	Task<T> Execute<T>(
		string endpointKey, Func<Uri, CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
