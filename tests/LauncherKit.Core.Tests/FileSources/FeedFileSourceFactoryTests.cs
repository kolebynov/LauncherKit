using System.Net;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class FeedFileSourceFactoryTests
{
	[Test]
	public async Task Create_OpeningMetadata_UsesPathUnderGivenBaseUrl()
	{
		// Arrange
		var directUri = new Uri("https://direct.test/base/dir/file.bin");
		var proxyUri = new Uri("https://proxy.test/dir/file.bin");
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Head, directUri.AbsoluteUri).Respond(_ => Response(10));
		handler.Expect(HttpMethod.Head, proxyUri.AbsoluteUri).Respond(_ => Response(20));
		var mocker = new AutoMocker(MockBehavior.Strict);
		MockHttpClientFactory.Factory(mocker, AppConstants.S3DownloadHttpClient, handler);
		mocker.Use<ILoggerFactory>(NullLoggerFactory.Instance);
		mocker.Use(TimeProvider.System);
		mocker.Use<IDownloadRateLimiter>(UnlimitedDownloadRateLimiter.Instance);
		var sut = mocker.CreateInstance<FeedFileSourceFactory>(enablePrivate: true);
		var download = new DownloadInfo { S3Path = "dir/file.bin" };

		// Act
		var directSize = await sut.Create(download, new Uri("https://direct.test/base/")).GetFileSize(CancellationToken.None);
		var proxySize = await sut.Create(download, new Uri("https://proxy.test/")).GetFileSize(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(directSize, Is.EqualTo(10));
			Assert.That(proxySize, Is.EqualTo(20));
		});
		handler.VerifyNoOutstandingExpectation();
	}

	private static HttpResponseMessage Response(long contentLength)
	{
		var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
		response.Content.Headers.ContentLength = contentLength;
		return response;
	}
}
