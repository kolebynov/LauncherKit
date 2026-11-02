using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using R3;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Settings;

namespace LauncherKit.Core.Discord;

internal sealed class DiscordAccountService : IDiscordAccountService, IDisposable
{
	/// <summary>How long <see cref="GetCachedLinkedAccount"/> serves an account read from Discord.</summary>
	public static readonly TimeSpan AccountCacheLifetime = TimeSpan.FromMinutes(5);

	/// <summary>
	/// How long <see cref="GetCachedLinkedAccount"/> rethrows a failed read, so an unreachable Discord does not delay
	/// every caller by a timeout.
	/// </summary>
	public static readonly TimeSpan FailedReadCacheLifetime = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Tokens expiring sooner are refreshed before use.
	/// </summary>
	private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

	private readonly IDiscordApiClient _apiClient;
	private readonly ILogger<DiscordAccountService> _logger;
	private readonly IDiscordOAuthClient _oauthClient;
	private readonly IOptions<DiscordSettings> _settings;
	private readonly TimeProvider _timeProvider;
	private readonly IOAuthTokenStore _tokenStore;
	private readonly Subject<Unit> _linkedAccountChangedSubject = new();

	/// <summary>
	/// Serializes reads and writes of the stored tokens: Discord rotates the refresh token on each refresh, so two
	/// concurrent refreshes would leave one of them with a revoked token.
	/// </summary>
	private readonly SemaphoreSlim _tokenLock = new(1, 1);

	private readonly Lock _cacheLock = new();
	private CachedAccount? _cachedAccount;

	/// <summary>
	/// Incremented whenever the link changes, so a read that started before the change does not cache its outdated
	/// result.
	/// </summary>
	private long _linkGeneration;

	public Observable<Unit> LinkedAccountChanged => _linkedAccountChangedSubject;

	public DiscordAccountService(IDiscordApiClient apiClient, IDiscordOAuthClient oauthClient,
		IOAuthTokenStore tokenStore, IOptions<DiscordSettings> settings, TimeProvider timeProvider,
		ILogger<DiscordAccountService> logger)
	{
		_apiClient = apiClient;
		_logger = logger;
		_oauthClient = oauthClient;
		_settings = settings;
		_timeProvider = timeProvider;
		_tokenStore = tokenStore;
	}

	public async Task<DiscordAccount?> GetLinkedAccount(CancellationToken cancellationToken)
	{
		var generation = Interlocked.Read(ref _linkGeneration);
		try
		{
			var account = await ReadLinkedAccount(cancellationToken);
			Cache(generation, new CachedAccount(account, null, _timeProvider.GetUtcNow() + AccountCacheLifetime));
			return account;
		}
		catch (Exception e) when (!cancellationToken.IsCancellationRequested)
		{
			Cache(generation, new CachedAccount(null, ExceptionDispatchInfo.Capture(e),
				_timeProvider.GetUtcNow() + FailedReadCacheLifetime));
			throw;
		}
	}

	public Task<DiscordAccount?> GetCachedLinkedAccount(CancellationToken cancellationToken)
	{
		lock (_cacheLock)
		{
			if (_cachedAccount is { } cached && _timeProvider.GetUtcNow() < cached.ExpiresAt)
			{
				cached.Error?.Throw();
				return Task.FromResult(cached.Account);
			}
		}

		return GetLinkedAccount(cancellationToken);
	}

	public async Task<DiscordAccount> Link(CancellationToken cancellationToken)
	{
		// Reading the guild ID first fails on missing settings before the browser opens.
		var guildId = GetGuildId();
		var token = await _oauthClient.Login(cancellationToken);
		var account = await GetAccount(token.AccessToken, guildId, cancellationToken);
		await _tokenLock.WaitAsync(cancellationToken);
		try
		{
			_tokenStore.Set(DiscordOAuthClient.ProviderName, token);
		}
		finally
		{
			_tokenLock.Release();
		}

		_logger.LogInformation("Linked Discord account {UserId}", account.User.Id);
		OnLinkChanged(account);
		return account;
	}

	public async Task Unlink(CancellationToken cancellationToken)
	{
		OAuthToken? token;
		await _tokenLock.WaitAsync(cancellationToken);
		try
		{
			try
			{
				token = _tokenStore.Get(DiscordOAuthClient.ProviderName);
			}
			catch (CryptographicException e)
			{
				_logger.LogWarning(e, "Failed to decrypt the Discord tokens, they cannot be revoked");
				token = null;
			}

			_tokenStore.Remove(DiscordOAuthClient.ProviderName);
		}
		finally
		{
			_tokenLock.Release();
		}

		OnLinkChanged(null);
		if (token is not null && await _oauthClient.Revoke(token, cancellationToken))
		{
			_logger.LogInformation("Revoked the Discord authorization");
		}
	}

	public void Dispose()
	{
		_tokenLock.Dispose();
		_linkedAccountChangedSubject.Dispose();
	}

	private async Task<DiscordAccount?> ReadLinkedAccount(CancellationToken cancellationToken)
	{
		var (token, forgotten) = await GetValidToken(cancellationToken);
		if (forgotten)
		{
			OnLinkChanged(null);
		}

		if (token is null)
		{
			return null;
		}

		try
		{
			return await GetAccount(token.AccessToken, cancellationToken);
		}
		catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
		{
			_logger.LogWarning(e, "Discord rejected the stored access token, forgetting the linked account");
			if (await RemoveToken(token, cancellationToken))
			{
				OnLinkChanged(null);
			}

			return null;
		}
	}

	/// <summary>
	/// Updates the cache and notifies <see cref="LinkedAccountChanged"/> subscribers; called outside the token lock,
	/// since subscribers may read the account again.
	/// </summary>
	private void OnLinkChanged(DiscordAccount? account)
	{
		lock (_cacheLock)
		{
			_linkGeneration++;
			_cachedAccount = new CachedAccount(account, null, _timeProvider.GetUtcNow() + AccountCacheLifetime);
		}

		_linkedAccountChangedSubject.OnNext(Unit.Default);
	}

	private void Cache(long generation, CachedAccount cachedAccount)
	{
		lock (_cacheLock)
		{
			if (_linkGeneration == generation)
			{
				_cachedAccount = cachedAccount;
			}
		}
	}

	/// <returns>
	/// The token to use, <see langword="null"/> when there is none, and whether a stored link was forgotten because
	/// it can no longer be used.
	/// </returns>
	private async Task<(OAuthToken? Token, bool Forgotten)> GetValidToken(CancellationToken cancellationToken)
	{
		await _tokenLock.WaitAsync(cancellationToken);
		try
		{
			OAuthToken? token;
			try
			{
				token = _tokenStore.Get(DiscordOAuthClient.ProviderName);
			}
			catch (CryptographicException e)
			{
				// Only the Windows user that linked the account can decrypt its tokens, so nobody can use them.
				_logger.LogWarning(e, "Failed to decrypt the Discord tokens, forgetting the linked account");
				_tokenStore.Remove(DiscordOAuthClient.ProviderName);
				return (null, true);
			}

			if (token is null || token.ExpiresAt - _timeProvider.GetUtcNow() > RefreshMargin)
			{
				return (token, false);
			}

			var refreshed = await _oauthClient.Refresh(token.RefreshToken, cancellationToken);
			if (refreshed is null)
			{
				_logger.LogWarning("Discord rejected the refresh token, forgetting the linked account");
				_tokenStore.Remove(DiscordOAuthClient.ProviderName);
				return (null, true);
			}

			_tokenStore.Set(DiscordOAuthClient.ProviderName, refreshed);
			return (refreshed, false);
		}
		finally
		{
			_tokenLock.Release();
		}
	}

	/// <summary>
	/// Removes <paramref name="token"/> unless the account was linked again meanwhile.
	/// </summary>
	/// <returns>Whether the token was removed.</returns>
	private async Task<bool> RemoveToken(OAuthToken token, CancellationToken cancellationToken)
	{
		await _tokenLock.WaitAsync(cancellationToken);
		try
		{
			if (_tokenStore.Get(DiscordOAuthClient.ProviderName) != token)
			{
				return false;
			}

			_tokenStore.Remove(DiscordOAuthClient.ProviderName);
			return true;
		}
		finally
		{
			_tokenLock.Release();
		}
	}

	private Task<DiscordAccount> GetAccount(string accessToken, CancellationToken cancellationToken) =>
		GetAccount(accessToken, GetGuildId(), cancellationToken);

	private async Task<DiscordAccount> GetAccount(string accessToken, ulong guildId, CancellationToken cancellationToken)
	{
		var user = await _apiClient.GetCurrentUser(accessToken, cancellationToken);
		var member = await _apiClient.GetCurrentUserGuildMember(accessToken, guildId, user.Id, cancellationToken);
		return new DiscordAccount(user, member);
	}

	private ulong GetGuildId() => ulong.Parse(_settings.Value.GuildId, CultureInfo.InvariantCulture);

	/// <summary>A read of the linked account: the account, or the failure to rethrow.</summary>
	private sealed record CachedAccount(DiscordAccount? Account, ExceptionDispatchInfo? Error, DateTimeOffset ExpiresAt);
}
