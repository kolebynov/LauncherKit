using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Models;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class HttpFileSourceFactoryTests
{
	[Test]
	public async Task Create_UriAndHash_SourceUsesBothValues()
	{
		var uri = new Uri("https://cdn.example.test/files/archive.7z");
		var hash = new Sha256Hash(new byte[32]);
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Head, uri.AbsoluteUri).Respond(_ => new HttpResponseMessage
		{
			Content = new ByteArrayContent(new byte[42]),
		});
		using var client = handler.ToHttpClient();
		var sut = new HttpFileSourceFactory(
			client, NullLoggerFactory.Instance, TimeProvider.System, new UnlimitedDownloadRateLimiter());

		var source = sut.Create(uri, hash);
		var size = await source.GetFileSize(CancellationToken.None);
		var actualHash = await source.GetHash(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(size, Is.EqualTo(42));
			Assert.That(actualHash, Is.EqualTo(hash));
		});
		handler.VerifyNoOutstandingExpectation();
	}
}
