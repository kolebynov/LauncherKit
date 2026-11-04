using Microsoft.Extensions.Options;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Settings;

namespace LauncherKit.Core.Services;

public sealed class UrlResolver : IUrlResolver
{
	private readonly IOptionsMonitor<UrlsSettings> _urlsSettings;
	private volatile DownloadServer _preferredServer = DownloadServer.Main;

	public UrlResolver(IOptionsMonitor<UrlsSettings> urlsSettings)
	{
		_urlsSettings = urlsSettings;
	}

	public DownloadServer PreferredServer
	{
		get => _preferredServer;
		set => _preferredServer = value;
	}

	public Uri GetUri(string key)
	{
		var settings = GetSettings(key);
		return PreferredServer == DownloadServer.Proxy && settings.ProxyUrl is { } proxyUrl ? proxyUrl : settings.MainUrl;
	}

	public Uri GetUri(string key, DownloadServer server)
	{
		var settings = GetSettings(key);
		return server switch
		{
			DownloadServer.Main => settings.MainUrl,
			DownloadServer.Proxy => settings.ProxyUrl
				?? throw new InvalidOperationException($"URL configuration with key '{key}' has no proxy server."),
			_ => throw new ArgumentOutOfRangeException(nameof(server), server, "Unknown download server"),
		};
	}

	public bool HasProxy(string key) => GetSettings(key).ProxyUrl != null;

	private UrlSettings GetSettings(string key)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);

		if (_urlsSettings.CurrentValue.Urls.TryGetValue(key, out var settings))
		{
			return settings;
		}

		throw new InvalidOperationException($"URL configuration with key '{key}' was not found.");
	}
}
