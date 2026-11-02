using LauncherKit.Core.Services;
using LauncherKit.Core.Settings;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class UrlResolverTests
{
	[Test]
	public void GetUri_NewResolver_UsesMainServer()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, CreateUrls());
		var sut = mocker.CreateInstance<UrlResolver>();

		// Act
		var result = sut.GetUri(UrlKeys.Feed);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.PreferredServer, Is.EqualTo(DownloadServer.Main));
			Assert.That(result, Is.EqualTo(new Uri("https://main.test/base/")));
		});
	}

	[TestCase(DownloadServer.Main, "https://main.test/base/")]
	[TestCase(DownloadServer.Proxy, "https://proxy.test/base/")]
	public void GetUri_PreferredServerSet_UsesItsEndpoint(DownloadServer server, string expected)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, CreateUrls());
		var sut = mocker.CreateInstance<UrlResolver>();
		sut.PreferredServer = server;

		// Act
		var result = sut.GetUri(UrlKeys.Feed);

		// Assert
		Assert.That(result, Is.EqualTo(new Uri(expected)));
	}

	[TestCase(DownloadServer.Main, "https://main.test/base/")]
	[TestCase(DownloadServer.Proxy, "https://proxy.test/base/")]
	public void GetUri_ExplicitServer_IgnoresPreferredServer(DownloadServer server, string expected)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, CreateUrls());
		var sut = mocker.CreateInstance<UrlResolver>();
		sut.PreferredServer = server == DownloadServer.Main ? DownloadServer.Proxy : DownloadServer.Main;

		// Act
		var result = sut.GetUri(UrlKeys.Feed, server);

		// Assert
		Assert.That(result, Is.EqualTo(new Uri(expected)));
	}

	[Test]
	public void GetUri_UnknownKey_ThrowsClearException()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, CreateUrls());
		var sut = mocker.CreateInstance<UrlResolver>();

		// Act
		// Assert
		Assert.That(() => sut.GetUri("missing"), Throws.InvalidOperationException.With.Message.Contains("missing"));
	}

	private static UrlsSettings CreateUrls() => new()
	{
		Urls = new Dictionary<string, UrlSettings>
		{
			[UrlKeys.Feed] = new()
			{
				MainUrl = new Uri("https://main.test/base/"),
				ProxyUrl = new Uri("https://proxy.test/base/"),
			},
		},
	};
}
