using System.Net;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Models;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class HttpFileSourceTests
{
	private static readonly Uri FileUri = new("https://files.test/archive.bin");
	private static readonly Sha256Hash Hash = new(new byte[32]);

	[Test]
	public async Task GetFileSize_CalledTwice_SendsSingleHeadRequest()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var request = handler.Expect(HttpMethod.Head, FileUri.AbsoluteUri);
		request.Respond(_ => Response(HttpStatusCode.OK, [], 123));
		using var client = handler.ToHttpClient();
		var sut = CreateSut(client);

		// Act
		var first = await sut.GetFileSize(CancellationToken.None);
		var second = await sut.GetFileSize(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(123));
			Assert.That(second, Is.EqualTo(123));
			Assert.That(handler.GetMatchCount(request), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task OpenStream_ResumedRead_SendsRangeAndAcceptsPartialContent()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var requestedVersions = new List<Version>();
		handler.Expect(HttpMethod.Get, FileUri.AbsoluteUri)
			.WithHeaders("Range", "bytes=2-5")
			.Respond(request =>
			{
				requestedVersions.Add(request.Version);
				return Response(HttpStatusCode.PartialContent, [3, 4, 5, 6], 4);
			});
		using var client = handler.ToHttpClient();
		client.DefaultRequestVersion = HttpVersion.Version20;
		var sut = CreateSut(client);

		// Act
		await using var stream = await sut.OpenStream(2, 5, CancellationToken.None);
		var buffer = new byte[4];
		var count = await stream.Read(buffer, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(count, Is.EqualTo(4));
			Assert.That(buffer, Is.EqualTo(new byte[] { 3, 4, 5, 6 }));
			Assert.That(stream.Position, Is.EqualTo(4));

			// Parts must not be multiplexed onto one connection, whatever the client default says.
			Assert.That(requestedVersions, Is.EqualTo(new[] { HttpVersion.Version11 }));
		});
	}

	[Test]
	public void OpenStream_RangeRequestReturnsOk_ThrowsUnsupportedRangeException()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, FileUri.AbsoluteUri).Respond(_ => Response(HttpStatusCode.OK, [3, 4, 5, 6], 4));
		using var client = handler.ToHttpClient();
		var sut = CreateSut(client);

		// Act
		// Assert
		Assert.That(async () => await sut.OpenStream(2, null, CancellationToken.None),
			Throws.InvalidOperationException.With.Message.Contains("range"));
	}

	[Test]
	public async Task OpenStream_WholeFileWithoutBounds_SendsNoRangeAndAcceptsOk()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, FileUri.AbsoluteUri)
			.With(request => request.Headers.Range == null)
			.Respond(_ => Response(HttpStatusCode.OK, [3, 4, 5, 6], 4));
		using var client = handler.ToHttpClient();
		var sut = CreateSut(client);

		// Act
		await using var stream = await sut.OpenStream(null, null, CancellationToken.None);
		var buffer = new byte[4];
		var count = await stream.Read(buffer, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(count, Is.EqualTo(4));
			Assert.That(buffer, Is.EqualTo(new byte[] { 3, 4, 5, 6 }));
		});
	}

	[Test]
	public async Task GetHash_ConstructedHash_ReturnsSameValue()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		using var client = handler.ToHttpClient();
		var sut = CreateSut(client);

		// Act
		var result = await sut.GetHash(CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo(Hash));
	}

	private static HttpFileSource CreateSut(HttpClient client)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(client);
		mocker.Use<ILoggerFactory>(NullLoggerFactory.Instance);
		mocker.Use<TimeProvider>(TimeProvider.System);
		mocker.Use<IDownloadRateLimiter>(UnlimitedDownloadRateLimiter.Instance);
		mocker.Use(FileUri);
		mocker.Use<Sha256Hash?>(Hash);
		return mocker.CreateInstance<HttpFileSource>(enablePrivate: true);
	}

	private static HttpResponseMessage Response(HttpStatusCode status, byte[] bytes, long contentLength)
	{
		var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
		response.Content.Headers.ContentLength = contentLength;
		return response;
	}
}
