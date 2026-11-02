using System.Net.Http.Json;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Analytics;

internal sealed class InstallationAnalyticsService : IInstallationAnalyticsService
{
	private const string InstallationPath = "v1/installations";

	private readonly IAppVersionProvider _appVersionProvider;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IInstallationIdProvider _installationIdProvider;
	private readonly IUrlResolver _urlResolver;

	public InstallationAnalyticsService(
		IAppVersionProvider appVersionProvider,
		IDownloadServerFallback downloadServerFallback,
		IHttpClientFactory httpClientFactory,
		IInstallationIdProvider installationIdProvider,
		IUrlResolver urlResolver)
	{
		_appVersionProvider = appVersionProvider;
		_downloadServerFallback = downloadServerFallback;
		_httpClientFactory = httpClientFactory;
		_installationIdProvider = installationIdProvider;
		_urlResolver = urlResolver;
	}

	public async Task ReportInstallation(CancellationToken cancellationToken)
	{
		var installationEvent = new InstallationEvent(
			_installationIdProvider.GetInstallationId(),
			_appVersionProvider.Version.ToString());
		using var httpClient = _httpClientFactory.CreateClient(AppConstants.AnalyticsHttpClient);

		await _downloadServerFallback.Execute(
			async (server, ct) =>
			{
				await Report(httpClient, _urlResolver.GetUri(UrlKeys.Analytics, server), installationEvent, ct);
				return true;
			},
			cancellationToken);
	}

	private static async Task Report(
		HttpClient httpClient, Uri baseUri, InstallationEvent installationEvent, CancellationToken cancellationToken)
	{
		using var response = await httpClient.PostAsJsonAsync(
			new Uri(baseUri, InstallationPath),
			installationEvent,
			AnalyticsJsonContext.Default.InstallationEvent,
			cancellationToken);
		response.EnsureSuccessStatusCode();
	}
}
