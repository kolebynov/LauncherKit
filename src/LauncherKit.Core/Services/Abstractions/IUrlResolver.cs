namespace LauncherKit.Core.Services.Abstractions;

public interface IUrlResolver
{
	/// <summary>
	/// The server tried first. Starts as <see cref="DownloadServer.Main"/> on each launcher run and is kept only
	/// in memory, so the resolver must be a singleton.
	/// </summary>
	DownloadServer PreferredServer { get; set; }

	/// <summary>
	/// The <paramref name="key"/>'s endpoint on <see cref="PreferredServer"/>, or on <see cref="DownloadServer.Main"/>
	/// when the key has no proxy.
	/// </summary>
	Uri GetUri(string key);

	/// <exception cref="InvalidOperationException">
	/// <paramref name="key"/> is not configured, or <paramref name="server"/> is the proxy and the key has none.
	/// </exception>
	Uri GetUri(string key, DownloadServer server);

	/// <summary>Whether <paramref name="key"/> has a <see cref="DownloadServer.Proxy"/> endpoint.</summary>
	bool HasProxy(string key);
}
