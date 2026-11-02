using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Core.Tests.Utils;

[TestFixture]
public sealed class FeedUtilsTests
{
	[Test]
	public void GetUri_RelativeFeedPath_ComposesWithoutLosingBasePath()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IUrlResolver>()
			.Setup(x => x.GetUri(UrlKeys.Feed))
			.Returns(new Uri("https://feed.test/releases/"));
		var sut = mocker.CreateInstance<FeedUtils>();

		// Act
		var result = sut.GetUri("v8/feed.json");

		// Assert
		Assert.That(result, Is.EqualTo(new Uri("https://feed.test/releases/v8/feed.json")));
	}

	[Test]
	public void GetUri_ExplicitServer_UsesThatServersBase()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var resolver = mocker.GetMock<IUrlResolver>();
		resolver.Setup(x => x.GetUri(UrlKeys.Feed, DownloadServer.Main)).Returns(new Uri("https://direct.test/"));
		resolver.Setup(x => x.GetUri(UrlKeys.Feed, DownloadServer.Proxy)).Returns(new Uri("https://proxy.test/"));
		var sut = mocker.CreateInstance<FeedUtils>();

		// Act
		var direct = sut.GetUri("feed.json", DownloadServer.Main);
		var proxy = sut.GetUri("feed.json", DownloadServer.Proxy);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(direct, Is.EqualTo(new Uri("https://direct.test/feed.json")));
			Assert.That(proxy, Is.EqualTo(new Uri("https://proxy.test/feed.json")));
		});
	}
}
