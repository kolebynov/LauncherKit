using System.Net;
using NuGet.Versioning;
using RichardSzalay.MockHttp;
using LauncherKit.Core.Analytics;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Analytics;

[TestFixture]
public sealed class InstallationAnalyticsServiceTests
{
	private const string MainEndpoint = "https://analytics.test/base/v1/installations";
	private const string ProxyEndpoint = "https://proxy.test/base/v1/installations";

	[TestCase("https://analytics.test/base/", MainEndpoint)]
	[TestCase("https://proxy.test/base/", ProxyEndpoint)]
	public async Task ReportInstallation_BaseUrlChosenByFallback_PostsCamelCasePayloadToItsEndpoint(
		string serverBase, string endpoint)
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var request = handler.Expect(HttpMethod.Post, endpoint)
			.WithContent("{\"installationId\":\"install-id\",\"launcherVersion\":\"1.2.3\"}");
		request.Respond(HttpStatusCode.NoContent);
		var sut = CreateSut(handler, new Uri(serverBase));

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
		var sut = CreateSut(handler, new Uri("https://analytics.test/base/"));

		// Act
		// Assert
		Assert.That(async () => await sut.ReportInstallation(CancellationToken.None),
			Throws.InstanceOf<HttpRequestException>());
	}

	private static InstallationAnalyticsService CreateSut(MockHttpMessageHandler handler, Uri baseUri)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		MockHttpClientFactory.Factory(mocker, AppConstants.AnalyticsHttpClient, handler);
		mocker.GetMock<IDownloadServerFallback>()
			.Setup(x => x.Execute(
				UrlKeys.Analytics, It.IsAny<Func<Uri, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
			.Returns((string _, Func<Uri, CancellationToken, Task<bool>> operation, CancellationToken ct) =>
				operation(baseUri, ct));
		mocker.GetMock<IInstallationIdProvider>()
			.Setup(x => x.GetInstallationId())
			.Returns("install-id");
		mocker.GetMock<IAppVersionProvider>()
			.SetupGet(x => x.Version)
			.Returns(NuGetVersion.Parse("1.2.3"));
		return mocker.CreateInstance<InstallationAnalyticsService>(enablePrivate: true);
	}
}
