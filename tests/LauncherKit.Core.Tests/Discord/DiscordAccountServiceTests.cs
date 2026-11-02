using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using R3;
using LauncherKit.Core.Discord;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Settings;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Discord;

[TestFixture]
public sealed class DiscordAccountServiceTests
{
	private const ulong GuildId = 42;

	private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

	private static readonly DiscordUser User =
		new(1, "nelly", "Nelly", new Uri("https://cdn.discordapp.com/embed/avatars/0.png"));

	private static readonly DiscordGuildMember Member = new(null, null, [7, 8]);

	[Test]
	public async Task GetLinkedAccount_NothingStored_ReturnsNull()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IOAuthTokenStore>().Setup(x => x.Get(DiscordOAuthClient.ProviderName)).Returns((OAuthToken?)null);
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		var account = await sut.GetLinkedAccount(CancellationToken.None);

		// Assert
		Assert.That(account, Is.Null);
	}

	[Test]
	public async Task GetLinkedAccount_TokenValid_ReturnsUserAndMembershipWithoutRefreshing()
	{
		// Arrange
		var mocker = CreateMocker();
		var token = new OAuthToken("access", "refresh", Now.AddDays(1));
		mocker.GetMock<IOAuthTokenStore>().Setup(x => x.Get(DiscordOAuthClient.ProviderName)).Returns(token);
		SetupAccount(mocker, "access");
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		var account = await sut.GetLinkedAccount(CancellationToken.None);

		// Assert
		Assert.That(account, Is.EqualTo(new DiscordAccount(User, Member)));
	}

	[Test]
	public async Task GetLinkedAccount_TokenAboutToExpire_StoresRefreshedTokenAndUsesIt()
	{
		// Arrange
		var mocker = CreateMocker();
		var store = mocker.GetMock<IOAuthTokenStore>();
		store.Setup(x => x.Get(DiscordOAuthClient.ProviderName))
			.Returns(new OAuthToken("old-access", "old-refresh", Now.AddSeconds(30)));
		var refreshed = new OAuthToken("new-access", "new-refresh", Now.AddDays(7));
		mocker.GetMock<IDiscordOAuthClient>().Setup(x => x.Refresh("old-refresh", CancellationToken.None))
			.ReturnsAsync(refreshed);
		store.Setup(x => x.Set(DiscordOAuthClient.ProviderName, refreshed));
		SetupAccount(mocker, "new-access");
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		var account = await sut.GetLinkedAccount(CancellationToken.None);

		// Assert
		Assert.That(account, Is.EqualTo(new DiscordAccount(User, Member)));
		store.Verify(x => x.Set(DiscordOAuthClient.ProviderName, refreshed), Times.Once);
	}

	[Test]
	public async Task GetLinkedAccount_RefreshTokenRejected_ForgetsLinkAndReturnsNull()
	{
		// Arrange
		var mocker = CreateMocker();
		var store = mocker.GetMock<IOAuthTokenStore>();
		store.Setup(x => x.Get(DiscordOAuthClient.ProviderName))
			.Returns(new OAuthToken("old-access", "old-refresh", Now.AddSeconds(-1)));
		mocker.GetMock<IDiscordOAuthClient>().Setup(x => x.Refresh("old-refresh", CancellationToken.None))
			.ReturnsAsync((OAuthToken?)null);
		store.Setup(x => x.Remove(DiscordOAuthClient.ProviderName));
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		var account = await sut.GetLinkedAccount(CancellationToken.None);

		// Assert
		Assert.That(account, Is.Null);
		store.Verify(x => x.Remove(DiscordOAuthClient.ProviderName), Times.Once);
	}

	[Test]
	public async Task GetLinkedAccount_AccessTokenRejected_ForgetsLinkAndReturnsNull()
	{
		// Arrange
		var mocker = CreateMocker();
		var store = mocker.GetMock<IOAuthTokenStore>();
		var token = new OAuthToken("access", "refresh", Now.AddDays(1));
		store.Setup(x => x.Get(DiscordOAuthClient.ProviderName)).Returns(token);
		store.Setup(x => x.Remove(DiscordOAuthClient.ProviderName));
		mocker.GetMock<IDiscordApiClient>().Setup(x => x.GetCurrentUser("access", CancellationToken.None))
			.ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.Unauthorized));
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		var account = await sut.GetLinkedAccount(CancellationToken.None);

		// Assert
		Assert.That(account, Is.Null);
		store.Verify(x => x.Remove(DiscordOAuthClient.ProviderName), Times.Once);
	}

	[Test]
	public void GetLinkedAccount_DiscordUnavailable_ThrowsAndKeepsLink()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IOAuthTokenStore>().Setup(x => x.Get(DiscordOAuthClient.ProviderName))
			.Returns(new OAuthToken("access", "refresh", Now.AddDays(1)));
		mocker.GetMock<IDiscordApiClient>().Setup(x => x.GetCurrentUser("access", CancellationToken.None))
			.ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.ServiceUnavailable));
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		// Assert
		// The strict store mock fails the test if the link is removed.
		Assert.That(async () => await sut.GetLinkedAccount(CancellationToken.None),
			Throws.InstanceOf<HttpRequestException>());
	}

	[Test]
	public async Task Link_UserAuthorizes_StoresTokenAndReturnsAccount()
	{
		// Arrange
		var mocker = CreateMocker();
		var token = new OAuthToken("access", "refresh", Now.AddDays(7));
		mocker.GetMock<IDiscordOAuthClient>().Setup(x => x.Login(CancellationToken.None)).ReturnsAsync(token);
		var store = mocker.GetMock<IOAuthTokenStore>();
		store.Setup(x => x.Set(DiscordOAuthClient.ProviderName, token));
		SetupAccount(mocker, "access");
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		var account = await sut.Link(CancellationToken.None);

		// Assert
		Assert.That(account, Is.EqualTo(new DiscordAccount(User, Member)));
		store.Verify(x => x.Set(DiscordOAuthClient.ProviderName, token), Times.Once);
	}

	[Test]
	public void Link_AccountCannotBeRead_DoesNotStoreToken()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IDiscordOAuthClient>().Setup(x => x.Login(CancellationToken.None))
			.ReturnsAsync(new OAuthToken("access", "refresh", Now.AddDays(7)));
		mocker.GetMock<IDiscordApiClient>().Setup(x => x.GetCurrentUser("access", CancellationToken.None))
			.ThrowsAsync(new HttpRequestException());
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		// Assert
		// The strict store mock fails the test if the token is stored.
		Assert.That(async () => await sut.Link(CancellationToken.None), Throws.InstanceOf<HttpRequestException>());
	}

	[Test]
	public async Task GetCachedLinkedAccount_WithinLifetime_ServesLastReadWithoutAskingDiscord()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IOAuthTokenStore>().Setup(x => x.Get(DiscordOAuthClient.ProviderName))
			.Returns(new OAuthToken("access", "refresh", Now.AddDays(1)));
		SetupAccount(mocker, "access");
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);
		await sut.GetCachedLinkedAccount(CancellationToken.None);
		((FakeTimeProvider)mocker.Get<TimeProvider>()).Advance(DiscordAccountService.AccountCacheLifetime - TimeSpan.FromSeconds(1));

		// Act
		var account = await sut.GetCachedLinkedAccount(CancellationToken.None);

		// Assert
		Assert.That(account, Is.EqualTo(new DiscordAccount(User, Member)));
		mocker.GetMock<IDiscordApiClient>().Verify(x => x.GetCurrentUser("access", CancellationToken.None), Times.Once);
	}

	[Test]
	public async Task GetCachedLinkedAccount_LifetimePassed_ReadsAgain()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IOAuthTokenStore>().Setup(x => x.Get(DiscordOAuthClient.ProviderName))
			.Returns(new OAuthToken("access", "refresh", Now.AddDays(1)));
		SetupAccount(mocker, "access");
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);
		await sut.GetCachedLinkedAccount(CancellationToken.None);
		((FakeTimeProvider)mocker.Get<TimeProvider>()).Advance(DiscordAccountService.AccountCacheLifetime);

		// Act
		await sut.GetCachedLinkedAccount(CancellationToken.None);

		// Assert
		mocker.GetMock<IDiscordApiClient>().Verify(x => x.GetCurrentUser("access", CancellationToken.None), Times.Exactly(2));
	}

	[Test]
	public void GetCachedLinkedAccount_ReadFailedRecently_RethrowsWithoutAskingDiscordAgain()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IOAuthTokenStore>().Setup(x => x.Get(DiscordOAuthClient.ProviderName))
			.Returns(new OAuthToken("access", "refresh", Now.AddDays(1)));
		var apiClient = mocker.GetMock<IDiscordApiClient>();
		apiClient.Setup(x => x.GetCurrentUser("access", CancellationToken.None)).ThrowsAsync(new HttpRequestException());
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);
		Assert.That(async () => await sut.GetCachedLinkedAccount(CancellationToken.None), Throws.InstanceOf<HttpRequestException>());

		// Act
		// Assert
		Assert.That(async () => await sut.GetCachedLinkedAccount(CancellationToken.None), Throws.InstanceOf<HttpRequestException>());
		apiClient.Verify(x => x.GetCurrentUser("access", CancellationToken.None), Times.Once);
	}

	[Test]
	public async Task Link_AfterCachedRead_ServesNewAccountAndRaisesLinkedAccountChanged()
	{
		// Arrange
		var mocker = CreateMocker();
		var store = mocker.GetMock<IOAuthTokenStore>();
		store.Setup(x => x.Get(DiscordOAuthClient.ProviderName)).Returns((OAuthToken?)null);
		var token = new OAuthToken("access", "refresh", Now.AddDays(7));
		mocker.GetMock<IDiscordOAuthClient>().Setup(x => x.Login(CancellationToken.None)).ReturnsAsync(token);
		store.Setup(x => x.Set(DiscordOAuthClient.ProviderName, token));
		SetupAccount(mocker, "access");
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);
		var cachedBefore = await sut.GetCachedLinkedAccount(CancellationToken.None);
		var raised = 0;
		using var subscription = sut.LinkedAccountChanged.Subscribe(_ => raised++);

		// Act
		await sut.Link(CancellationToken.None);
		var cachedAfter = await sut.GetCachedLinkedAccount(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(cachedBefore, Is.Null);
			Assert.That(cachedAfter, Is.EqualTo(new DiscordAccount(User, Member)));
			Assert.That(raised, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Unlink_RevocationFails_StillForgetsLink()
	{
		// Arrange
		var mocker = CreateMocker();
		var store = mocker.GetMock<IOAuthTokenStore>();
		var token = new OAuthToken("access", "refresh", Now.AddDays(1));
		store.Setup(x => x.Get(DiscordOAuthClient.ProviderName)).Returns(token);
		store.Setup(x => x.Remove(DiscordOAuthClient.ProviderName));
		mocker.GetMock<IDiscordOAuthClient>().Setup(x => x.Revoke(token, CancellationToken.None)).ReturnsAsync(false);
		using var sut = mocker.CreateInstance<DiscordAccountService>(enablePrivate: true);

		// Act
		await sut.Unlink(CancellationToken.None);

		// Assert
		store.Verify(x => x.Remove(DiscordOAuthClient.ProviderName), Times.Once);
		mocker.GetMock<IDiscordOAuthClient>().Verify(x => x.Revoke(token, CancellationToken.None), Times.Once);
	}

	private static AutoMocker CreateMocker()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<DiscordAccountService>>(NullLogger<DiscordAccountService>.Instance);
		mocker.Use<TimeProvider>(new FakeTimeProvider(Now));
		OptionsStubs.Options(mocker, new DiscordSettings { ClientId = "1", GuildId = "42", RedirectPort = 53134 });
		return mocker;
	}

	private static void SetupAccount(AutoMocker mocker, string accessToken)
	{
		var apiClient = mocker.GetMock<IDiscordApiClient>();
		apiClient.Setup(x => x.GetCurrentUser(accessToken, CancellationToken.None)).ReturnsAsync(User);
		apiClient.Setup(x => x.GetCurrentUserGuildMember(accessToken, GuildId, User.Id, CancellationToken.None))
			.ReturnsAsync(Member);
	}
}
