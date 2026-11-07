using System.Net;
using LauncherKit.Core;
using LauncherKit.Desktop.Services;
using LauncherKit.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;

namespace LauncherKit.Desktop.Tests.Services;

[TestFixture]
public sealed class MarkdownLoaderTests
{
	private static readonly Uri Source = new("https://feed.test/Changelogs/first.md");

	[Test]
	public async Task TryLoad_Success_ReturnsText()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, Source.AbsoluteUri).Respond("text/markdown", "# First");
		var sut = CreateSut(handler);

		// Act
		var text = await sut.TryLoad(Source, CancellationToken.None);

		// Assert
		Assert.That(text, Is.EqualTo("# First"));
	}

	[Test]
	public async Task TryLoad_ErrorStatus_ReturnsNullInsteadOfErrorPage()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, Source.AbsoluteUri).Respond(HttpStatusCode.NotFound, "text/html", "<html>Not Found</html>");
		var sut = CreateSut(handler);

		// Act
		var text = await sut.TryLoad(Source, CancellationToken.None);

		// Assert
		Assert.That(text, Is.Null);
	}

	[Test]
	public void TryLoad_Cancelled_Throws()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, Source.AbsoluteUri).Respond("text/markdown", "# First");
		var sut = CreateSut(handler);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		// Act & Assert
		Assert.That(async () => await sut.TryLoad(Source, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
	}

	private static MarkdownLoader CreateSut(MockHttpMessageHandler handler)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<MarkdownLoader>>(NullLogger<MarkdownLoader>.Instance);
		MockHttpClientFactory.Factory(mocker, AppConstants.S3HttpClient, handler);
		return mocker.CreateInstance<MarkdownLoader>();
	}
}
