namespace LauncherKit.Core.Services.Abstractions;

public interface IUrlResolver
{
	/// <summary>
	/// The server tried first. Starts as <see cref="DownloadServer.Main"/> on each launcher run and is kept only
	/// in memory, so the resolver must be a singleton.
	/// </summary>
	DownloadServer PreferredServer { get; set; }

	Uri GetUri(string key);

	Uri GetUri(string key, DownloadServer server);
}
