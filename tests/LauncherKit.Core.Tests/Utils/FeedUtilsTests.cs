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

}
