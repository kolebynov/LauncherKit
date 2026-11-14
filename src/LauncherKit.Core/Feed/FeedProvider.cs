using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using R3;
using LauncherKit.Core.Discord;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Settings;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Feed;

public sealed class FeedProvider : IFeedProvider, IDisposable
{
	private const string ActivatedBetaCodesKey = "ActivatedBetaCodes";

	public async ValueTask<IReadOnlyList<InstallComponent>> GetInstallComponents(CancellationToken cancellationToken) =>
		(await GetFeed(cancellationToken)).InstallComponents;

	public async ValueTask<IReadOnlyList<Prerequisite>> GetPrerequisites(CancellationToken cancellationToken) =>
		(await GetFeed(cancellationToken)).Prerequisites;

	/// <summary>How long a loaded feed is served from the cache.</summary>
	public static readonly TimeSpan FeedLifetime = TimeSpan.FromMinutes(1);

	/// <summary>
	/// How long the default feed is served after a failed load: short enough for the next page visit to retry, long
	/// enough for the burst of reads of one page to share a single failed load.
	/// </summary>
	public static readonly TimeSpan FailedFeedLifetime = TimeSpan.FromSeconds(15);

	private static readonly FeedData DefaultFeedData = new()
	{
		DefaultLanguage = "ru", Launchers = [],
		SevenZipDownloadInfo = DownloadInfo.Empty,
		SkyrimDownloadInfo = DownloadInfo.Empty,
		Modpacks = [],
		ModpackChangelogs = [],
	};

	private readonly IDiscordAccountService _discordAccountService;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IGlobalKeyValueStorage _keyValueStorage;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<FeedProvider> _logger;
	private readonly Subject<Unit> _betaAccessChangedSubject = new();
	private readonly Subject<FeedLoadEvent> _loadEventsSubject = new();
	private readonly IDisposable _linkedAccountSubscription;
	private readonly CancellationTokenSource _disposeCts = new();
	private readonly Lock _feedLock = new();
	private readonly string _feedPath;
	private Task<CachedFeed>? _feedTask;

	public Observable<Unit> BetaAccessChanged => _betaAccessChangedSubject;

	public Observable<FeedLoadEvent> LoadEvents => _loadEventsSubject;

	public FeedProvider(IDownloadServerFallback downloadServerFallback,
		IGlobalKeyValueStorage keyValueStorage, IHttpClientFactory httpClientFactory, TimeProvider timeProvider,
		IDiscordAccountService discordAccountService, IOptions<FeedSettings> feedSettings, ILogger<FeedProvider> logger)
	{
		_downloadServerFallback = downloadServerFallback;
		_keyValueStorage = keyValueStorage;
		_httpClientFactory = httpClientFactory;
		_timeProvider = timeProvider;
		_discordAccountService = discordAccountService;
		_logger = logger;
		_feedPath = feedSettings.Value.FeedPath;
		_linkedAccountSubscription = discordAccountService.LinkedAccountChanged
			.Subscribe(_betaAccessChangedSubject, static (_, subject) => subject.OnNext(Unit.Default));
	}

	public async ValueTask<ActivateBetaCodeResult> ActivateBetaCode(string betaCode, CancellationToken cancellationToken)
	{
		var activatedBetaCodes = GetActivatedBetaCodes();
		if (activatedBetaCodes.Contains(betaCode))
		{
			return ActivateBetaCodeResult.AlreadyActivated.Instance;
		}

		var hashedBetaCode = HashBetaCode(betaCode);
		var feed = await GetFeed(cancellationToken);
		if (feed.BetaVersions?.ContainsKey(hashedBetaCode) ?? false)
		{
			_keyValueStorage.Set(
				ActivatedBetaCodesKey,
				JsonSerializer.Serialize([..activatedBetaCodes, betaCode], SourceGeneratedJsonContext.Default.StringArray));
			_betaAccessChangedSubject.OnNext(Unit.Default);
			return ActivateBetaCodeResult.Success.Instance;
		}

		return ActivateBetaCodeResult.InvalidBetaCode.Instance;
	}

	public async ValueTask<IEnumerable<ModpackFeedItem>> GetModpackFeedItems(CancellationToken cancellationToken)
	{
		var activatedBetaCodes = GetHashedActivatedBetaCodes();
		var feedData = await GetFeed(cancellationToken);
		BetaRole? betaRole = null;
		var offeredItems = new List<ModpackFeedItem>(feedData.Modpacks.Count);
		foreach (var modpackFeed in feedData.Modpacks)
		{
			if (!modpackFeed.Version.IsPrerelease)
			{
				offeredItems.Add(modpackFeed);
				continue;
			}

			var isUnlockedByBetaCode = feedData.BetaVersions?.Any(x =>
				activatedBetaCodes.Contains(x.Key) && x.Value.Satisfies(modpackFeed.Version)) ?? false;
			if (isUnlockedByBetaCode)
			{
				offeredItems.Add(modpackFeed);
				continue;
			}

			// A prerelease no activated beta code covers (including one no beta code covers at all) is offered only to
			// developers and, when marked, to sponsors. Discord is asked only when a role could unlock it.
			if (!modpackFeed.IsAvailableForSponsors && feedData.DeveloperRoleIds is not { Count: > 0 })
			{
				continue;
			}

			betaRole ??= await GetBetaRole(feedData, cancellationToken);
			if (betaRole == BetaRole.Developer || (betaRole == BetaRole.Sponsor && modpackFeed.IsAvailableForSponsors))
			{
				offeredItems.Add(modpackFeed);
			}
		}

		return offeredItems;
	}

	public async ValueTask<BetaAccessRoles> GetBetaAccessRoles(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return new BetaAccessRoles(feedData.SponsorRoleIds ?? [], feedData.DeveloperRoleIds ?? []);
	}

	public async ValueTask<IReadOnlyList<LauncherFeedItem>> GetLauncherFeedItems(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return feedData.Launchers;
	}

	public async ValueTask<IReadOnlyList<ModFeedData>> GetModFeedDatas(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return feedData.Mods;
	}

	public async ValueTask<IReadOnlyList<ModCategoryFeedData>> GetModCategories(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return feedData.ModCategories;
	}

	public async ValueTask<IReadOnlyList<ModpackChangelogFeedItem>> GetModpackChangelogs(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return feedData.ModpackChangelogs;
	}

	public async ValueTask<DownloadInfo> GetSevenZipDownloadInfo(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return feedData.SevenZipDownloadInfo;
	}

	public async ValueTask<DownloadInfo> GetSkyrimDownloadInfo(CancellationToken cancellationToken)
	{
		var feedData = await GetFeed(cancellationToken);
		return feedData.SkyrimDownloadInfo;
	}

	public void Dispose()
	{
		_disposeCts.Cancel();
		_linkedAccountSubscription.Dispose();
		_betaAccessChangedSubject.Dispose();
		_loadEventsSubject.Dispose();
		_disposeCts.Dispose();
	}

	/// <summary>
	/// The beta access role of the linked Discord account. Discord being unreachable only hides the versions the roles
	/// unlock, so it is logged rather than failing the feed.
	/// </summary>
	private async Task<BetaRole> GetBetaRole(FeedData feedData, CancellationToken cancellationToken)
	{
		if (feedData.SponsorRoleIds is not { Count: > 0 } && feedData.DeveloperRoleIds is not { Count: > 0 })
		{
			return BetaRole.None;
		}

		try
		{
			var account = await _discordAccountService.GetCachedLinkedAccount(cancellationToken);
			return account switch
			{
				null => BetaRole.None,
				_ when account.HasAnyRole(feedData.DeveloperRoleIds ?? []) => BetaRole.Developer,
				_ when account.HasAnyRole(feedData.SponsorRoleIds ?? []) => BetaRole.Sponsor,
				_ => BetaRole.None,
			};
		}
		catch (Exception e) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning(e, "Failed to read the linked Discord account, sponsor and developer beta versions are hidden");
			return BetaRole.None;
		}
	}

	private ValueTask<FeedData> GetFeed(CancellationToken cancellationToken)
	{
		Task<CachedFeed> feedTask;
		lock (_feedLock)
		{
			ObjectDisposedException.ThrowIf(_disposeCts.IsCancellationRequested, this);
			if (_feedTask is null || (_feedTask.IsCompletedSuccessfully && _timeProvider.GetUtcNow() >= _feedTask.Result.ExpiresAt))
			{
				// The load is shared by every caller, so it runs without any caller's token: a caller that gives up only
				// stops waiting. Task.Run keeps the load and its events outside the lock.
				_feedTask = Task.Run(LoadFeed, CancellationToken.None);
			}

			feedTask = _feedTask;
		}

		return feedTask.IsCompletedSuccessfully
			? ValueTask.FromResult(feedTask.Result.Feed)
			: WaitForFeed(feedTask, cancellationToken);

		static async ValueTask<FeedData> WaitForFeed(Task<CachedFeed> feedTask, CancellationToken cancellationToken) =>
			(await feedTask.WaitAsync(cancellationToken)).Feed;
	}

	private async Task<CachedFeed> LoadFeed()
	{
		var disposeToken = _disposeCts.Token;
		_loadEventsSubject.OnNext(FeedLoadEvent.Started.Instance);
		try
		{
			var feed = await _downloadServerFallback.Execute(
				UrlKeys.Feed,
				async (feedBaseUri, ct) =>
				{
					using var httpClient = _httpClientFactory.CreateClient(AppConstants.S3HttpClient);
					await using var stream = await httpClient.GetStreamAsync(new Uri(feedBaseUri, _feedPath), ct);
					return (await JsonSerializer.DeserializeAsync(stream, SourceGeneratedJsonContext.Default.FeedData, ct))!;
				},
				disposeToken);
			FeedValidator.Validate(feed);
			_loadEventsSubject.OnNext(FeedLoadEvent.Succeeded.Instance);
			return new CachedFeed(feed, _timeProvider.GetUtcNow() + FeedLifetime);
		}
		catch (Exception e) when (!disposeToken.IsCancellationRequested)
		{
			_logger.LogError(e, "Failed to load feed. Return default feed");
			_loadEventsSubject.OnNext(new FeedLoadEvent.Failed(e));
			return new CachedFeed(DefaultFeedData, _timeProvider.GetUtcNow() + FailedFeedLifetime);
		}
	}

	private string[] GetActivatedBetaCodes()
	{
		var activatedBetaCodesStr = _keyValueStorage.Get(ActivatedBetaCodesKey);
		return !string.IsNullOrEmpty(activatedBetaCodesStr)
			? JsonSerializer.Deserialize<string[]>(activatedBetaCodesStr, SourceGeneratedJsonContext.Default.StringArray)!
			: [];
	}

	private string[] GetHashedActivatedBetaCodes() => GetActivatedBetaCodes().Select(HashBetaCode).ToArray();

	public static string HashBetaCode(string betaCode)
	{
		var byteCount = Encoding.UTF8.GetByteCount(betaCode);
		Span<byte> toHash = stackalloc byte[byteCount + 4];
		var status = Utf8.FromUtf16(betaCode, toHash, out var charsRead, out var bytesWritten);
		if (status != OperationStatus.Done || byteCount != bytesWritten || betaCode.Length != charsRead)
		{
			throw new InvalidOperationException(
				$"Invalid string to UTF-8 conversion. Operation status: {status}, read: {charsRead}, written: {bytesWritten}, string length: {betaCode.Length}");
		}

		toHash[^4] = 10;
		toHash[^3] = 100;
		toHash[^2] = 128;
		toHash[^1] = 250;

		return Convert.ToHexString(SHA256.HashData(toHash));
	}

	private sealed record CachedFeed(FeedData Feed, DateTimeOffset ExpiresAt);

	private enum BetaRole
	{
		None,
		Sponsor,
		Developer,
	}
}