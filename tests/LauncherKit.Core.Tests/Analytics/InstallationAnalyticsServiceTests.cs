using System.Net;
using NuGet.Versioning;
using RichardSzalay.MockHttp;
using LauncherKit.Core.Analytics;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Analytics;

[TestFixture]
public sealed class InstallationAnalyticsServiceTests
{
	private const string MainEndpoint = "https://analytics.test/base/v1/installations";
	private const string ProxyEndpoint = "https://proxy.test/base/v1/installations";

	[TestCase(DownloadServer.Main, MainEndpoint)]
	[TestCase(DownloadServer.Proxy, ProxyEndpoint)]
	public async Task ReportInstallation_ServerChosenByFallback_PostsCamelCasePayloadToItsEndpoint(
		DownloadServer server, string endpoint)
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var request = handler.Expect(HttpMethod.Post, endpoint)
			.WithContent("{\"installationId\":\"install-id\",\"launcherVersion\":\"1.2.3\"}");
		request.Respond(HttpStatusCode.NoContent);
		var sut = CreateSut(handler, server);

		// Act
		await sut.ReportInstallation(CancellationToken.None);

		// Assert
		Assert.That(handler.GetMatchCount(request), Is.EqualTo(1));
	}

	[Test]
	public void ReportInstallation_EndpointReturnsError_ThrowsHttpRequestExceptionToFallback()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Post, MainEndpoint).Respond(HttpStatusCode.ServiceUnavailable);
		var sut = CreateSut(handler, DownloadServer.Main);

		// Act
		// Assert
		Assert.That(async () => await sut.ReportInstallation(CancellationToken.None),
			Throws.InstanceOf<HttpRequestException>());
	}

	private static InstallationAnalyticsService CreateSut(MockHttpMessageHandler handler, DownloadServer server)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		MockHttpClientFactory.Factory(mocker, AppConstants.AnalyticsHttpClient, handler);
		mocker.GetMock<IDownloadServerFallback>()
			.Setup(x => x.Execute(
				It.IsAny<Func<DownloadServer, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
			.Returns((Func<DownloadServer, CancellationToken, Task<bool>> operation, CancellationToken ct) =>
				operation(server, ct));
		var urlResolver = mocker.GetMock<IUrlResolver>();
		urlResolver.Setup(x => x.GetUri(UrlKeys.Analytics, DownloadServer.Main))
			.Returns(new Uri("https://analytics.test/base/"));
		urlResolver.Setup(x => x.GetUri(UrlKeys.Analytics, DownloadServer.Proxy))
			.Returns(new Uri("https://proxy.test/base/"));
		mocker.GetMock<IInstallationIdProvider>()
			.Setup(x => x.GetInstallationId())
			.Returns("install-id");
		mocker.GetMock<IAppVersionProvider>()
			.SetupGet(x => x.Version)
			.Returns(NuGetVersion.Parse("1.2.3"));
		return mocker.CreateInstance<InstallationAnalyticsService>(enablePrivate: true);
	}
}
