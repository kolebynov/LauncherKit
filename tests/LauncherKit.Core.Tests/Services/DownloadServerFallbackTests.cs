using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class DownloadServerFallbackTests
{
	[Test]
	public async Task Execute_PreferredServerWorks_ReturnsItsResultWithoutTryingOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		var servers = new List<DownloadServer>();

		// Act
		var result = await sut.Execute(
			(server, _) =>
			{
				servers.Add(server);
				return Task.FromResult(42);
			},
			CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(42));
			Assert.That(servers, Is.EqualTo(new[] { DownloadServer.Main }));
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
			(server, _) => server == preferredServer
				? Task.FromException<DownloadServer>(new HttpRequestException("offline"))
				: Task.FromResult(server),
			CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(otherServer));
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
			(server, _) => Task.FromException<int>(server == DownloadServer.Main ? mainException : proxyException),
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
		var servers = new List<DownloadServer>();

		// Act
		await Assert.ThrowsAsync<NotEnoughSpaceException>(() => sut.Execute<int>(
			(server, _) =>
			{
				servers.Add(server);
				return Task.FromException<int>(new NotEnoughSpaceException("C:", 10, 1));
			},
			CancellationToken.None));

		// Assert
		Assert.That(servers, Is.EqualTo(new[] { DownloadServer.Main }));
	}

	[Test]
	public void Execute_CallerCancels_PropagatesCancellationWithoutTryingOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);
		using var cts = new CancellationTokenSource();
		var servers = new List<DownloadServer>();

		// Act
		Assert.That(
			() => sut.Execute<int>(
				(server, ct) =>
				{
					servers.Add(server);
					cts.Cancel();
					ct.ThrowIfCancellationRequested();
					return Task.FromResult(0);
				},
				cts.Token),
			Throws.InstanceOf<OperationCanceledException>());

		// Assert
		Assert.That(servers, Is.EqualTo(new[] { DownloadServer.Main }));
	}

	[Test]
	public async Task Execute_TimeoutNotRequestedByCaller_TriesOtherServer()
	{
		// Arrange
		var mocker = CreateMocker(DownloadServer.Main);
		var sut = mocker.CreateInstance<DownloadServerFallback>(enablePrivate: true);

		// Act
		var result = await sut.Execute(
			(server, _) => server == DownloadServer.Main
				? Task.FromException<DownloadServer>(new TaskCanceledException("request timed out"))
				: Task.FromResult(server),
			CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo(DownloadServer.Proxy));
	}

	private static AutoMocker CreateMocker(DownloadServer preferredServer)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<DownloadServerFallback>>(NullLogger<DownloadServerFallback>.Instance);
		mocker.GetMock<IUrlResolver>().SetupProperty(x => x.PreferredServer, preferredServer);
		return mocker;
	}
}
