namespace LauncherKit.Core.Services.Abstractions;

public interface IDownloadServerFallback
{
	/// <summary>
	/// Runs <paramref name="operation"/> against the preferred server and, when it fails because of the server
	/// (failed request, broken or stalled transfer, timeout), against the other server. When the other server
	/// succeeds it becomes <see cref="IUrlResolver.PreferredServer"/> for the rest of the launcher run.
	/// </summary>
	/// <exception cref="Exceptions.DownloadServersUnavailableException">Both servers failed.</exception>
	Task<T> Execute<T>(
		Func<DownloadServer, CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
