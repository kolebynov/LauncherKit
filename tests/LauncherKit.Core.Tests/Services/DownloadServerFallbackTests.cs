using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class DownloadServerFallbackTests
{
	private static readonly Uri MainUri = new("https://main.test/base/");
	private static readonly Uri ProxyUri = new("https://proxy.test/base/");

	[Test]
	public async Task Execute_PreferredServerWorks_ReturnsItsResultWithoutTryingOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		var uris = new List<Uri>();

		// Act
		var result = await sut.Execute(
			UrlKeys.Feed,
			(uri, _) =>
			{
				uris.Add(uri);
				return Task.FromResult(42);
			},
			CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(42));
			Assert.That(uris, Is.EqualTo(new[] { MainUri }));
			Assert.That(mocker.Get<IUrlResolver>().PreferredServer, Is.EqualTo(DownloadServer.Main));
		});
	}

	[TestCase(DownloadServer.Main, DownloadServer.Proxy)]
	[TestCase(DownloadServer.Proxy, DownloadServer.Main)]
	public async Task Execute_PreferredServerFailsAndOtherWorks_MakesOtherServerPreferred(
		DownloadServer preferredServer, DownloadServer otherServer)
	{
		// Arrange
		var mocker = CreateMocker(preferredServer);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);

		// Act
		var result = await sut.Execute(
			UrlKeys.Feed,
			(uri, _) => uri == UriOf(preferredServer)
				? Task.FromException<Uri>(new HttpRequestException("offline"))
				: Task.FromResult(uri),
			CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(UriOf(otherServer)));
			Assert.That(mocker.Get<IUrlResolver>().PreferredServer, Is.EqualTo(otherServer));
		});
	}

	[TestCase(DownloadServer.Main)]
	[TestCase(DownloadServer.Proxy)]
	public async Task Execute_BothServersFail_ThrowsWithEachServersFailure(DownloadServer preferredServer)
	{
		// Arrange
		var mocker = CreateMocker(preferredServer);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		var mainException = new HttpRequestException("main offline");
		var proxyException = new DownloadStalledException(TimeSpan.FromSeconds(30));

		// Act
		var exception = await Assert.ThrowsAsync<DownloadServersUnavailableException>(() => sut.Execute<int>(
			UrlKeys.Feed,
			(uri, _) => Task.FromException<int>(uri == MainUri ? mainException : proxyException),
			CancellationToken.None));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(exception!.MainServerException, Is.SameAs(mainException));
			Assert.That(exception.ProxyServerException, Is.SameAs(proxyException));
			Assert.That(mocker.Get<IUrlResolver>().PreferredServer, Is.EqualTo(preferredServer));
		});
	}

	[Test]
	public async Task Execute_FailureNotCausedByServer_PropagatesWithoutTryingOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		var uris = new List<Uri>();

		// Act
		await Assert.ThrowsAsync<NotEnoughSpaceException>(() => sut.Execute<int>(
			UrlKeys.Feed,
			(uri, _) =>
			{
				uris.Add(uri);
				return Task.FromException<int>(new NotEnoughSpaceException("C:", 10, 1));
			},
			CancellationToken.None));

		// Assert
		Assert.That(uris, Is.EqualTo(new[] { MainUri }));
	}

	[Test]
	public void Execute_CallerCancels_PropagatesCancellationWithoutTryingOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		using var cts = new CancellationTokenSource();
		var uris = new List<Uri>();

		// Act
		Assert.That(
			() => sut.Execute<int>(
				UrlKeys.Feed,
				(uri, ct) =>
				{
					uris.Add(uri);
					cts.Cancel();
					ct.ThrowIfCancellationRequested();
					return Task.FromResult(0);
				},
				cts.Token),
			Throws.InstanceOf<OperationCanceledException>());

		// Assert
		Assert.That(uris, Is.EqualTo(new[] { MainUri }));
	}

	[Test]
	public async Task Execute_TimeoutNotRequestedByCaller_TriesOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);

		// Act
		var result = await sut.Execute(
			UrlKeys.Feed,
			(uri, _) => uri == MainUri
				? Task.FromException<Uri>(new TaskCanceledException("request timed out"))
				: Task.FromResult(uri),
			CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo(ProxyUri));
	}

	[TestCase(DownloadServer.Main)]
	[TestCase(DownloadServer.Proxy)]
	public async Task Execute_KeyWithoutProxy_RunsOnMainServerOnlyAndKeepsPreferredServer(DownloadServer preferredServer)
	{
		// Arrange
		var mocker = CreateMocker(preferredServer, hasProxy: false);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		var uris = new List<Uri>();

		// Act
		var result = await sut.Execute(
			UrlKeys.Feed,
			(uri, _) =>
			{
				uris.Add(uri);
				return Task.FromResult(42);
			},
			CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(42));
			Assert.That(uris, Is.EqualTo(new[] { MainUri }));
			Assert.That(mocker.Get<IUrlResolver>().PreferredServer, Is.EqualTo(preferredServer));
		});
	}

	[Test]
	public async Task Execute_KeyWithoutProxyAndMainServerFails_ThrowsServersUnavailableWithoutRetrying()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main, hasProxy: false);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		var mainException = new HttpRequestException("main offline");
		var uris = new List<Uri>();

		// Act
		var exception = await Assert.ThrowsAsync<DownloadServersUnavailableException>(() => sut.Execute<int>(
			UrlKeys.Feed,
			(uri, _) =>
			{
				uris.Add(uri);
				return Task.FromException<int>(mainException);
			},
			CancellationToken.None));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(uris, Is.EqualTo(new[] { MainUri }));
			Assert.That(exception!.MainServerException, Is.SameAs(mainException));
			Assert.That(exception.ProxyServerException, Is.Null);
		});
	}

	private static Uri UriOf(DownloadServer server) => server == DownloadServer.Main ? MainUri : ProxyUri;

	private static AutoMocker CreateMocker(DownloadServer preferredServer, bool hasProxy = true)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<DownloadServerFallback>>(NullLogger<DownloadServerFallback>.Instance);
		var urlResolver = mocker.GetMock<IUrlResolver>();
		urlResolver.SetupProperty(x => x.PreferredServer, preferredServer);
		urlResolver.Setup(x => x.HasProxy(UrlKeys.Feed)).Returns(hasProxy);
		urlResolver.Setup(x => x.GetUri(UrlKeys.Feed, DownloadServer.Main)).Returns(MainUri);
		urlResolver.Setup(x => x.GetUri(UrlKeys.Feed, DownloadServer.Proxy)).Returns(ProxyUri);
		return mocker;
	}
}
