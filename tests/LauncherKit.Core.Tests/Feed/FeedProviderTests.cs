using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using R3;
using RichardSzalay.MockHttp;
using LauncherKit.Core.Discord;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Settings;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Feed;

[TestFixture]
public sealed class FeedProviderTests
{
	private const string FeedPath = "feed.json";
	private const string FeedUrl = "https://feed.test/feed.json";

	[Test]
	public async Task ActivateBetaCode_AlreadyStored_ReturnsAlreadyActivatedWithoutFetchingFeed()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IGlobalKeyValueStorage>()
			.Setup(x => x.Get("ActivatedBetaCodes"))
			.Returns("[\"early-access\"]");
		OptionsStubs.Options(mocker, new FeedSettings { FeedPath = FeedPath });
		mocker.GetMock<IDiscordAccountService>().SetupGet(x => x.LinkedAccountChanged).Returns(Observable.Empty<Unit>());
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var result = await sut.ActivateBetaCode("early-access", CancellationToken.None);

		// Assert
		var caseName = result switch
		{
			ActivateBetaCodeResult.Success => "success",
			ActivateBetaCodeResult.InvalidBetaCode => "invalid",
			ActivateBetaCodeResult.AlreadyActivated => "already",
		};
		Assert.That(caseName, Is.EqualTo("already"));
		mocker.GetMock<IDownloadServerFallback>().VerifyNoOtherCalls();
	}

	[Test]
	public void HashBetaCode_SameUnicodeInput_IsStableAndCaseSensitive()
	{
		// Act
		var first = FeedProvider.HashBetaCode("Бета-Code");
		var second = FeedProvider.HashBetaCode("Бета-Code");
		var differentCase = FeedProvider.HashBetaCode("бета-Code");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(second));
			Assert.That(first, Is.Not.EqualTo(differentCase));
			Assert.That(first, Does.Match("^[0-9A-F]{64}$"));
		});
	}

	[Test]
	public async Task GetFeedData_FeedRequestFails_ReturnsDefaultFeed()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond(HttpStatusCode.InternalServerError);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var launchers = await sut.GetLauncherFeedItems(CancellationToken.None);
		var stb = await sut.GetModpackFeedItems(CancellationToken.None);
		var changelogs = await sut.GetModpackChangelogs(CancellationToken.None);
		var mods = await sut.GetModFeedDatas(CancellationToken.None);
		var graphPacks = await sut.GetGraphPacks(CancellationToken.None);
		var skyrim = await sut.GetSkyrimDownloadInfo(CancellationToken.None);
		var sevenZip = await sut.GetSevenZipDownloadInfo(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(launchers, Is.Empty);
			Assert.That(stb, Is.Empty);
			Assert.That(changelogs, Is.Empty);
			Assert.That(mods, Is.Null);
			Assert.That(graphPacks, Is.Empty);
			Assert.That(skyrim, Is.SameAs(DownloadInfo.Empty));
			Assert.That(sevenZip, Is.SameAs(DownloadInfo.Empty));
		});
	}

	[Test]
	public async Task GetLauncherFeedItems_FeedPayloadIsInvalidJson_ReturnsDefaultFeed()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", "{ not json");
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var launchers = await sut.GetLauncherFeedItems(CancellationToken.None);

		// Assert
		Assert.That(launchers, Is.Empty);
	}

	[Test]
	public async Task GetModpackFeedItems_SponsorAccountLinked_AddsOnlyBetaVersionsAvailableForSponsors()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", SponsorFeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetCachedLinkedAccount(CancellationToken.None))
			.ReturnsAsync(Account(222, SponsorRoleId));
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var versions = await GetModpackVersions(sut);

		// Assert
		Assert.That(versions, Is.EqualTo(new[] { "3.1.0", "3.2.0-beta.1", "3.3.0-alpha.1" }));
	}

	[Test]
	public async Task GetModpackFeedItems_LinkedAccountWithoutRoles_HidesAllPrereleases()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", SponsorFeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetCachedLinkedAccount(CancellationToken.None))
			.ReturnsAsync(Account(222));
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var versions = await GetModpackVersions(sut);

		// Assert
		Assert.That(versions, Is.EqualTo(new[] { "3.1.0" }));
	}

	[Test]
	public async Task GetModpackFeedItems_DiscordUnreachable_HidesRoleVersionsWithoutFailing()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", SponsorFeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetCachedLinkedAccount(CancellationToken.None))
			.ThrowsAsync(new HttpRequestException());
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var versions = await GetModpackVersions(sut);

		// Assert
		Assert.That(versions, Is.EqualTo(new[] { "3.1.0" }));
	}

	[Test]
	public async Task GetModpackFeedItems_DeveloperAccountLinked_AddsAllBetaVersions()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", SponsorFeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetCachedLinkedAccount(CancellationToken.None))
			.ReturnsAsync(Account(222, DeveloperRoleId));
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var versions = await GetModpackVersions(sut);

		// Assert
		Assert.That(versions,
			Is.EqualTo(new[] { "3.1.0", "3.2.0-beta.1", "3.2.0-beta.2", "3.3.0-alpha.1", "3.3.0-alpha.2" }));
	}

	[Test]
	public async Task GetModpackFeedItems_BetaCodeActivated_ShowsOnlyVersionsTheCodeCovers()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", SponsorFeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.Get("ActivatedBetaCodes")).Returns("[\"early-access\"]");
		mocker.GetMock<IDiscordAccountService>().Setup(x => x.GetCachedLinkedAccount(CancellationToken.None))
			.ReturnsAsync((DiscordAccount?)null);
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var versions = await GetModpackVersions(sut);

		// Assert
		Assert.That(versions, Is.EqualTo(new[] { "3.1.0", "3.2.0-beta.1", "3.2.0-beta.2" }));
	}

	[Test]
	public async Task GetModpackFeedItems_PrereleaseWithoutBetaCodesOrRoles_HidesItWithoutAskingDiscord()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", """
			{
				"DefaultLanguage": "ru", "SkyrimDownloadInfo": { "S3Path": "skyrim/stb.7z" },
				"SevenZipDownloadInfo": { "S3Path": "tools/7z.exe" },
				"Modpacks": [
					{ "InitialVersion": "3.1.0", "Version": "3.1.0", "DownloadInfo": { "S3Path": "stb/3.1.0.7z" } },
					{ "InitialVersion": "3.2.0-beta.1", "Version": "3.2.0-beta.1", "DownloadInfo": { "S3Path": "stb/b1.7z" } }
				],
				"ModpackChangelogs": [],
				"Launchers": []
			}
			""");
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var versions = await GetModpackVersions(sut);

		// Assert
		// The strict account service mock fails the test if Discord is asked.
		Assert.That(versions, Is.EqualTo(new[] { "3.1.0" }));
	}

	[Test]
	public void BetaAccessChanged_LinkedAccountChanged_IsRaised()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var linkedAccountChanged = new Subject<Unit>();
		mocker.GetMock<IDiscordAccountService>().SetupGet(x => x.LinkedAccountChanged).Returns(linkedAccountChanged);
		using var sut = mocker.CreateInstance<FeedProvider>();
		var raised = 0;
		using var subscription = sut.BetaAccessChanged.Subscribe(_ => raised++);

		// Act
		linkedAccountChanged.OnNext(Unit.Default);

		// Assert
		Assert.That(raised, Is.EqualTo(1));
	}

	[Test]
	public async Task GetLauncherFeedItems_FeedRequestSucceeds_ReturnsCachedFeedContent()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, FeedUrl).Respond("application/json", FeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();

		// Act
		var launchers = await sut.GetLauncherFeedItems(CancellationToken.None);
		var skyrimFromCachedFeed = await sut.GetSkyrimDownloadInfo(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(launchers.Select(x => x.Version.ToNormalizedString()), Is.EqualTo(new[] { "1.2.3" }));
			Assert.That(skyrimFromCachedFeed.S3Path, Is.EqualTo("skyrim/stb.7z"));
		});
		handler.VerifyNoOutstandingExpectation();
	}

	[Test]
	public async Task GetLauncherFeedItems_FeedLifetimePassed_ReloadsFeed()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var request = handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", FeedJson);
		var timeProvider = new FakeTimeProvider();
		var mocker = CreateMocker(handler, timeProvider);
		using var sut = mocker.CreateInstance<FeedProvider>();
		await sut.GetLauncherFeedItems(CancellationToken.None);

		// Act
		timeProvider.Advance(FeedProvider.FeedLifetime - TimeSpan.FromSeconds(1));
		await sut.GetLauncherFeedItems(CancellationToken.None);
		var requestsBeforeExpiry = handler.GetMatchCount(request);
		timeProvider.Advance(TimeSpan.FromSeconds(1));
		await sut.GetLauncherFeedItems(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(requestsBeforeExpiry, Is.EqualTo(1));
			Assert.That(handler.GetMatchCount(request), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task GetLauncherFeedItems_LoadFailedAndFailedFeedLifetimePassed_RetriesAndReturnsFeed()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Get, FeedUrl).Respond(HttpStatusCode.InternalServerError);
		handler.Expect(HttpMethod.Get, FeedUrl).Respond("application/json", FeedJson);
		var timeProvider = new FakeTimeProvider();
		var mocker = CreateMocker(handler, timeProvider);
		using var sut = mocker.CreateInstance<FeedProvider>();
		var afterFailure = await sut.GetLauncherFeedItems(CancellationToken.None);

		// Act
		timeProvider.Advance(FeedProvider.FailedFeedLifetime - TimeSpan.FromSeconds(1));
		var beforeRetry = await sut.GetLauncherFeedItems(CancellationToken.None);
		timeProvider.Advance(TimeSpan.FromSeconds(1));
		var afterRetry = await sut.GetLauncherFeedItems(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(afterFailure, Is.Empty);
			Assert.That(beforeRetry, Is.Empty);
			Assert.That(afterRetry.Select(x => x.Version.ToNormalizedString()), Is.EqualTo(new[] { "1.2.3" }));
		});
		handler.VerifyNoOutstandingExpectation();
	}

	[Test]
	public async Task GetLauncherFeedItems_CallerCancelledWhileLoading_OtherCallersShareTheLoadAndGetFeed()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var responseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var request = handler.When(HttpMethod.Get, FeedUrl);
		request.Respond(async () =>
		{
			await responseGate.Task;
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FeedJson) };
		});
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();
		using var cancellationTokenSource = new CancellationTokenSource();
		var cancelledCall = sut.GetLauncherFeedItems(cancellationTokenSource.Token).AsTask();
		var otherCall = sut.GetLauncherFeedItems(CancellationToken.None).AsTask();

		// Act
		await cancellationTokenSource.CancelAsync();
		responseGate.SetResult();

		// Assert
		Assert.That(async () => await cancelledCall, Throws.InstanceOf<OperationCanceledException>());
		var launchers = await otherCall;
		Assert.Multiple(() =>
		{
			Assert.That(launchers.Select(x => x.Version.ToNormalizedString()), Is.EqualTo(new[] { "1.2.3" }));
			Assert.That(handler.GetMatchCount(request), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetLauncherFeedItems_DisposedWhileLoading_ThrowsInsteadOfReturningDefaultFeed()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		mocker.GetMock<IDownloadServerFallback>()
			.Setup(x => x.Execute(
				It.IsAny<string>(), It.IsAny<Func<Uri, CancellationToken, Task<FeedData>>>(), It.IsAny<CancellationToken>()))
			.Returns(async (string _, Func<Uri, CancellationToken, Task<FeedData>> _, CancellationToken ct) =>
			{
				requestStarted.SetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, ct);
				throw new InvalidOperationException("Unreachable");
			});
		var sut = mocker.CreateInstance<FeedProvider>();
		var events = new List<string>();
		using var subscription = sut.LoadEvents.Subscribe(e => events.Add(Describe(e)));
		var call = sut.GetLauncherFeedItems(CancellationToken.None).AsTask();
		await requestStarted.Task;

		// Act
		sut.Dispose();

		// Assert
		Assert.That(async () => await call, Throws.InstanceOf<OperationCanceledException>());
		Assert.That(events, Is.EqualTo(new[] { "started" }));
	}

	[Test]
	public async Task LoadEvents_FeedLoadedThenReadFromCache_RaisesStartedAndSucceededOnce()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", FeedJson);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();
		var events = new List<string>();
		using var subscription = sut.LoadEvents.Subscribe(e => events.Add(Describe(e)));

		// Act
		await sut.GetLauncherFeedItems(CancellationToken.None);
		await sut.GetSkyrimDownloadInfo(CancellationToken.None);

		// Assert
		Assert.That(events, Is.EqualTo(new[] { "started", "succeeded" }));
	}

	[Test]
	public async Task LoadEvents_FeedRequestFails_RaisesStartedAndFailedWithTheError()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond(HttpStatusCode.InternalServerError);
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();
		var events = new List<string>();
		using var subscription = sut.LoadEvents.Subscribe(e => events.Add(Describe(e)));

		// Act
		await sut.GetLauncherFeedItems(CancellationToken.None);

		// Assert
		Assert.That(events, Is.EqualTo(new[] { "started", $"failed: {nameof(HttpRequestException)}" }));
	}

	[TestCase("""{ "$type": "UninstallMod", "Name": "a" }""", nameof(JsonException), TestName = "LoadEvents_FeedActionWithUnknownType_RaisesFailedAndReturnsDefaultFeed")]
	[TestCase("""{ "ArchiveName": "a" }""", nameof(NotSupportedException), TestName = "LoadEvents_FeedActionWithoutType_RaisesFailedAndReturnsDefaultFeed")]
	[TestCase("""{ "DownloadInfo": { "S3Path": "a.7z" }, "ModName": "A", "$type": "InstallMod" }""", nameof(NotSupportedException), TestName = "LoadEvents_FeedActionWithTypeNotFirst_RaisesFailedAndReturnsDefaultFeed")]
	public async Task LoadEvents_FeedActionNotReadable_RaisesFailedAndReturnsDefaultFeed(string action, string exceptionName)
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Get, FeedUrl).Respond("application/json", FeedJsonWithModAction(action));
		var mocker = CreateMocker(handler, new FakeTimeProvider());
		using var sut = mocker.CreateInstance<FeedProvider>();
		var events = new List<string>();
		using var subscription = sut.LoadEvents.Subscribe(e => events.Add(Describe(e)));

		// Act
		var launchers = await sut.GetLauncherFeedItems(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(events, Is.EqualTo(new[] { "started", $"failed: {exceptionName}" }));
			Assert.That(launchers, Is.Empty);
		});
	}

	private static string Describe(FeedLoadEvent loadEvent) =>
		loadEvent switch
		{
			FeedLoadEvent.Started => "started",
			FeedLoadEvent.Succeeded => "succeeded",
			FeedLoadEvent.Failed(var exception) => $"failed: {exception.GetType().Name}",
		};

	private static AutoMocker CreateMocker(MockHttpMessageHandler handler, TimeProvider timeProvider)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(timeProvider);
		mocker.Use<ILogger<FeedProvider>>(NullLogger<FeedProvider>.Instance);
		MockHttpClientFactory.Factory(mocker, AppConstants.S3HttpClient, handler);
		OptionsStubs.Options(mocker, new FeedSettings { FeedPath = FeedPath });
		DownloadServerFallbackStubs.SingleServer<FeedData>(mocker, new Uri("https://feed.test/"));
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.Get("ActivatedBetaCodes")).Returns((string?)null);
		mocker.GetMock<IDiscordAccountService>().SetupGet(x => x.LinkedAccountChanged).Returns(Observable.Empty<Unit>());
		return mocker;
	}

	private const string FeedJson = """
		{
			"DefaultLanguage": "ru", "SkyrimDownloadInfo": { "S3Path": "skyrim/stb.7z" },
			"SevenZipDownloadInfo": { "S3Path": "tools/7z.exe" },
			"Modpacks": [],
			"ModpackChangelogs": [],
			"Launchers": [
				{ "Version": "1.2.3", "DownloadInfo": { "S3Path": "launcher/1.2.3.7z" } }
			]
		}
		""";

	private static string FeedJsonWithModAction(string action) => $$"""
		{
			"DefaultLanguage": "ru", "SkyrimDownloadInfo": { "S3Path": "skyrim/stb.7z" },
			"SevenZipDownloadInfo": { "S3Path": "tools/7z.exe" },
			"Modpacks": [],
			"ModpackChangelogs": [],
			"Launchers": [
				{ "Version": "1.2.3", "DownloadInfo": { "S3Path": "launcher/1.2.3.7z" } }
			],
			"Mods": [{
				"Name": "Mod", "LogoPath": "l", "DescriptionPath": {"ru": "d"},
				"Items": [{
					"Version": "1.0.0", "SupportedModpackVersion": "[1.0.0, 2.0.0)", "ArchiveDownloadInfo": { "S3Path": "mod.7z" },
					"InstallSteps": [{ "Actions": [{{action}}] }]
				}]
			}]
		}
		""";

	private const ulong SponsorRoleId = 197038439483310086;

	private const ulong DeveloperRoleId = 41771983423143936;

	/// <summary>
	/// A release, two beta versions behind the "early-access" code (only the first for sponsors), two prereleases no
	/// code covers (only the first for sponsors), and one sponsor and one developer role.
	/// </summary>
	private static readonly string SponsorFeedJson = $$"""
		{
			"DefaultLanguage": "ru", "SkyrimDownloadInfo": { "S3Path": "skyrim/stb.7z" },
			"SevenZipDownloadInfo": { "S3Path": "tools/7z.exe" },
			"Modpacks": [
				{ "InitialVersion": "3.1.0", "Version": "3.1.0", "DownloadInfo": { "S3Path": "stb/3.1.0.7z" } },
				{ "InitialVersion": "3.2.0-beta.1", "Version": "3.2.0-beta.1", "DownloadInfo": { "S3Path": "stb/b1.7z" }, "IsAvailableForSponsors": true },
				{ "InitialVersion": "3.2.0-beta.2", "Version": "3.2.0-beta.2", "DownloadInfo": { "S3Path": "stb/b2.7z" } },
				{ "InitialVersion": "3.3.0-alpha.1", "Version": "3.3.0-alpha.1", "DownloadInfo": { "S3Path": "stb/a1.7z" }, "IsAvailableForSponsors": true },
				{ "InitialVersion": "3.3.0-alpha.2", "Version": "3.3.0-alpha.2", "DownloadInfo": { "S3Path": "stb/a2.7z" } }
			],
			"ModpackChangelogs": [],
			"Launchers": [],
			"BetaVersions": { "{{FeedProvider.HashBetaCode("early-access")}}": "[3.2.0-beta.1, 3.2.0)" },
			"SponsorRoleIds": [ "{{SponsorRoleId}}" ],
			"DeveloperRoleIds": [ "{{DeveloperRoleId}}" ]
		}
		""";

	private static async Task<string[]> GetModpackVersions(FeedProvider sut) =>
		(await sut.GetModpackFeedItems(CancellationToken.None)).Select(x => x.Version.ToNormalizedString()).ToArray();

	private static DiscordAccount Account(params ulong[] roleIds) => new(
		new DiscordUser(1, "nelly", "Nelly", new Uri("https://cdn.discordapp.com/embed/avatars/0.png")),
		new DiscordGuildMember(null, null, roleIds));
}
