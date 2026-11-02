using System.Globalization;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Settings;

namespace LauncherKit.Core.Discord;

internal sealed class DiscordOAuthClient : IDiscordOAuthClient
{
	public const string ProviderName = "Discord";
	public const string RedirectPath = "/discord/callback";

	private const string Authority = "https://discord.com";

	/// <summary>
	/// <c>openid</c> is required by <see cref="OidcClient"/>; <c>identify</c> reads the user and
	/// <c>guilds.members.read</c> the membership in the configured server.
	/// </summary>
	private const string Scope = "openid identify guilds.members.read";

	private static readonly Uri RevocationEndpoint = new("https://discord.com/api/oauth2/token/revoke");

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger<DiscordOAuthClient> _logger;
	private readonly IOptions<DiscordSettings> _settings;
	private readonly Lazy<OidcClient> _oidcClient;

	public DiscordOAuthClient(IBrowser browser, IHttpClientFactory httpClientFactory, IOptions<DiscordSettings> settings,
		ILogger<DiscordOAuthClient> logger)
	{
		_httpClientFactory = httpClientFactory;
		_logger = logger;
		_settings = settings;
		_oidcClient = new Lazy<OidcClient>(() => new OidcClient(new OidcClientOptions
		{
			Authority = Authority,
			ClientId = settings.Value.ClientId,
			Scope = Scope,
			RedirectUri = string.Create(CultureInfo.InvariantCulture,
				$"http://127.0.0.1:{settings.Value.RedirectPort}{RedirectPath}"),
			Browser = browser,
			LoadProfile = false,
			TokenClientCredentialStyle = ClientCredentialStyle.PostBody,
			HttpClientFactory = _ => httpClientFactory.CreateClient(AppConstants.DiscordHttpClient),
			Policy =
			{
				// The ID token comes straight from the token endpoint over TLS and is not used.
				RequireIdentityTokenSignature = false,
				RequireAccessTokenHash = false,
			},
		}));
	}

	public async Task<OAuthToken> Login(CancellationToken cancellationToken)
	{
		var result = await _oidcClient.Value.LoginAsync(new LoginRequest(), cancellationToken);
		if (result.IsError)
		{
			throw new OAuthAuthorizationException(ProviderName, result.Error, result.ErrorDescription);
		}

		return new OAuthToken(result.AccessToken, result.RefreshToken, result.AccessTokenExpiration);
	}

	public async Task<OAuthToken?> Refresh(string refreshToken, CancellationToken cancellationToken)
	{
		var result = await _oidcClient.Value.RefreshTokenAsync(refreshToken, cancellationToken: cancellationToken);
		if (!result.IsError)
		{
			return new OAuthToken(result.AccessToken, result.RefreshToken, result.AccessTokenExpiration);
		}

		if (result.Error == OidcConstants.TokenErrors.InvalidGrant)
		{
			return null;
		}

		throw new OAuthAuthorizationException(ProviderName, result.Error, result.ErrorDescription);
	}

	public async Task<bool> Revoke(OAuthToken token, CancellationToken cancellationToken)
	{
		using var httpClient = _httpClientFactory.CreateClient(AppConstants.DiscordHttpClient);
		var revoked = true;
		foreach (var (value, hint) in new[]
		         {
			         (token.RefreshToken, OidcConstants.TokenTypes.RefreshToken),
			         (token.AccessToken, OidcConstants.TokenTypes.AccessToken),
		         })
		{
			using var request = new TokenRevocationRequest
			{
				Address = RevocationEndpoint.AbsoluteUri,
				ClientId = _settings.Value.ClientId,
				ClientCredentialStyle = ClientCredentialStyle.PostBody,
				Token = value,
				TokenTypeHint = hint,
			};
			TokenRevocationResponse response;
			try
			{
				response = await httpClient.RevokeTokenAsync(request, cancellationToken);
			}
			catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
			{
				// RevokeTokenAsync turns transport failures into error responses but rethrows cancellations,
				// which include the HttpClient timeout.
				_logger.LogWarning(e, "Revoking the Discord {TokenType} timed out", hint);
				revoked = false;
				continue;
			}

			if (response.IsError)
			{
				_logger.LogWarning(response.Exception, "Failed to revoke the Discord {TokenType}: {Error}", hint,
					response.Error);
				revoked = false;
			}
		}

		return revoked;
	}
}
