using System.Net;
using System.Net.Sockets;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.OAuth;

[TestFixture]
public sealed class LoopbackBrowserTests
{
	private const string StartUrl = "https://provider.test/authorize?state=abc";

	[Test]
	public async Task InvokeAsync_BrowserRequestsOtherPathsFirst_ReturnsRedirectAndAnswersEachRequest()
	{
		// Arrange
		var redirectUri = $"http://127.0.0.1:{GetFreePort()}/oauth/callback";
		using var httpClient = new HttpClient();
		var responses = new List<HttpStatusCode>();
		string? completedPage = null;
		var mocker = CreateMocker();
		mocker.GetMock<IExternalBrowser>().Setup(x => x.Open(new Uri(StartUrl))).Returns(async () =>
		{
			// The browser follows the redirect asynchronously once the page is open.
			_ = Task.Run(async () =>
			{
				using var favicon = await httpClient.GetAsync(new Uri(new Uri(redirectUri), "/favicon.ico"));
				responses.Add(favicon.StatusCode);
				using var callback = await httpClient.GetAsync(new Uri($"{redirectUri}?code=xyz&state=abc"));
				completedPage = await callback.Content.ReadAsStringAsync();
				responses.Add(callback.StatusCode);
			});
			await Task.CompletedTask;
		});
		var sut = mocker.CreateInstance<LoopbackBrowser>(enablePrivate: true);

		// Act
		var result = await sut.InvokeAsync(new BrowserOptions(StartUrl, redirectUri) { Timeout = TimeSpan.FromSeconds(30) });

		// Assert
		Assert.That(result.ResultType, Is.EqualTo(BrowserResultType.Success));
		Assert.That(result.Response, Is.EqualTo($"{redirectUri}?code=xyz&state=abc"));
		Assert.That(() => responses, Is.EqualTo(new[] { HttpStatusCode.NotFound, HttpStatusCode.OK }).After(5000, 10));
		Assert.That(() => completedPage, Does.Contain("<title>Authorization &lt;complete&gt;</title>")
			.And.Contain("<h2>Authorization &lt;complete&gt;</h2>")
			.And.Contain("<p>Return &amp; close.</p>").After(5000, 10));
	}

	[Test]
	public async Task InvokeAsync_NoRedirectWithinTimeout_ReturnsTimeout()
	{
		// Arrange
		var redirectUri = $"http://127.0.0.1:{GetFreePort()}/oauth/callback";
		var mocker = CreateMocker();
		mocker.GetMock<IExternalBrowser>().Setup(x => x.Open(new Uri(StartUrl))).Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<LoopbackBrowser>(enablePrivate: true);

		// Act
		var result = await sut.InvokeAsync(new BrowserOptions(StartUrl, redirectUri) { Timeout = TimeSpan.FromMilliseconds(100) });

		// Assert
		Assert.That(result.ResultType, Is.EqualTo(BrowserResultType.Timeout));
	}

	[Test]
	public void InvokeAsync_Cancelled_ThrowsOperationCanceledException()
	{
		// Arrange
		var redirectUri = $"http://127.0.0.1:{GetFreePort()}/oauth/callback";
		using var cancellation = new CancellationTokenSource();
		var mocker = CreateMocker();
		mocker.GetMock<IExternalBrowser>().Setup(x => x.Open(new Uri(StartUrl))).Returns(() =>
		{
			cancellation.Cancel();
			return Task.CompletedTask;
		});
		var sut = mocker.CreateInstance<LoopbackBrowser>(enablePrivate: true);

		// Act
		// Assert
		Assert.That(
			async () => await sut.InvokeAsync(new BrowserOptions(StartUrl, redirectUri) { Timeout = TimeSpan.FromSeconds(30) },
				cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public void InvokeAsync_RedirectPortInUse_ThrowsWithoutOpeningBrowser()
	{
		// Arrange
		using var occupant = new TcpListener(IPAddress.Loopback, 0);
		occupant.Start();
		var port = ((IPEndPoint)occupant.LocalEndpoint).Port;
		var sut = CreateMocker().CreateInstance<LoopbackBrowser>(enablePrivate: true);

		// Act
		// Assert
		// The strict browser mock fails the test if the page is opened.
		Assert.That(
			async () => await sut.InvokeAsync(new BrowserOptions(StartUrl, $"http://127.0.0.1:{port}/oauth/callback")),
			Throws.InstanceOf<SocketException>()
				.With.Property(nameof(SocketException.SocketErrorCode)).EqualTo(SocketError.AddressAlreadyInUse));
	}

	[Test]
	public async Task InvokeAsync_ReusedBrowser_ResolvesCompletionTextForEachAuthorization()
	{
		using var httpClient = new HttpClient();
		var mocker = CreateMocker();
		var title = "First language";
		mocker.GetMock<IOptionsFactory<LoopbackBrowserOptions>>()
			.Setup(x => x.Create(Options.DefaultName))
			.Returns(() => new LoopbackBrowserOptions { Title = title, Message = title });
		var redirectUri = string.Empty;
		Task<string>? pageRequest = null;
		mocker.GetMock<IExternalBrowser>().Setup(x => x.Open(new Uri(StartUrl))).Returns(() =>
		{
			pageRequest = httpClient.GetStringAsync($"{redirectUri}?code=xyz");
			return Task.CompletedTask;
		});
		var sut = mocker.CreateInstance<LoopbackBrowser>(enablePrivate: true);

		foreach (var localizedTitle in new[] { "First language", "Second language" })
		{
			title = localizedTitle;
			redirectUri = $"http://127.0.0.1:{GetFreePort()}/oauth/callback";
			var result = await sut.InvokeAsync(new BrowserOptions(StartUrl, redirectUri) { Timeout = TimeSpan.FromSeconds(30) });
			var page = await pageRequest!;
			Assert.That(result.ResultType, Is.EqualTo(BrowserResultType.Success));
			Assert.That(page, Does.Contain($"<title>{localizedTitle}</title>").And.Contain($"<p>{localizedTitle}</p>"));
		}
	}

	private static AutoMocker CreateMocker()
	{
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<LoopbackBrowser>>(NullLogger<LoopbackBrowser>.Instance);
		mocker.GetMock<IOptionsFactory<LoopbackBrowserOptions>>()
			.Setup(x => x.Create(Options.DefaultName))
			.Returns(() => new LoopbackBrowserOptions { Title = "Authorization <complete>", Message = "Return & close." });
		return mocker;
	}

	private static int GetFreePort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}
}
