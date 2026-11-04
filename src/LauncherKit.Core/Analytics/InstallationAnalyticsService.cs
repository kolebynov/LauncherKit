using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Analytics;

internal sealed class InstallationAnalyticsService : IInstallationAnalyticsService
{
	private const string InstallationPath = "v1/installations";

	private readonly IAppVersionProvider _appVersionProvider;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IInstallationIdProvider _installationIdProvider;

	public InstallationAnalyticsService(
		IAppVersionProvider appVersionProvider,
		IDownloadServerFallback downloadServerFallback,
		IHttpClientFactory httpClientFactory,
		IInstallationIdProvider installationIdProvider)
	{
		_appVersionProvider = appVersionProvider;
		_downloadServerFallback = downloadServerFallback;
		_httpClientFactory = httpClientFactory;
		_installationIdProvider = installationIdProvider;
	}

	public async Task ReportInstallation(CancellationToken cancellationToken)
	{
		var installationEvent = new InstallationEvent(
			_installationIdProvider.GetInstallationId(),
			_appVersionProvider.Version.ToString());
		using var httpClient = _httpClientFactory.CreateClient(AppConstants.AnalyticsHttpClient);

		await _downloadServerFallback.Execute(
			UrlKeys.Analytics,
			async (baseUri, ct) =>
			{
				await Report(httpClient, baseUri, installationEvent, ct);
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
