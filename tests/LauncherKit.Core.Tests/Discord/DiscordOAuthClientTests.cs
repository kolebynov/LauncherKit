using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Duende.IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using LauncherKit.Core.Discord;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Settings;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Discord;

[TestFixture]
public sealed class DiscordOAuthClientTests
{
	private const string ClientId = "1234567890";
	private const string RedirectUri = "http://127.0.0.1:53134/discord/callback";
	private const string TokenEndpoint = "https://discord.com/api/oauth2/token";
	private const string RevocationEndpoint = "https://discord.com/api/oauth2/token/revoke";

	[Test]
	public async Task Login_UserAuthorizes_ExchangesCodeWithPkceVerifierAndNoSecret()
	{
		// Arrange
		using var handler = CreateDiscoveryHandler();
		var browser = new AuthorizingBrowser();
		IReadOnlyDictionary<string, string>? tokenForm = null;
		string? authorization = null;
		handler.Expect(HttpMethod.Post, TokenEndpoint).Respond(async request =>
		{
			authorization = request.Headers.Authorization?.ToString();
			tokenForm = await ReadForm(request);
			return TokenResponse("access", "refresh", CreateIdToken(browser.Nonce!));
		});
		var sut = CreateSut(handler, browser);

		// Act
		var token = await sut.Login(CancellationToken.None);

		// Assert
		Assert.That(token.AccessToken, Is.EqualTo("access"));
		Assert.That(token.RefreshToken, Is.EqualTo("refresh"));
		Assert.That(browser.Parameters!["client_id"], Is.EqualTo(ClientId));
		Assert.That(browser.Parameters["redirect_uri"], Is.EqualTo(RedirectUri));
		Assert.That(browser.Parameters["scope"], Is.EqualTo("openid identify guilds.members.read"));
		Assert.That(browser.Parameters["code_challenge_method"], Is.EqualTo("S256"));
		Assert.That(tokenForm, Is.Not.Null);
		Assert.That(tokenForm!["grant_type"], Is.EqualTo("authorization_code"));
		Assert.That(tokenForm["code"], Is.EqualTo(AuthorizingBrowser.Code));
		Assert.That(tokenForm["client_id"], Is.EqualTo(ClientId));
		Assert.That(tokenForm["redirect_uri"], Is.EqualTo(RedirectUri));
		Assert.That(tokenForm.ContainsKey("client_secret"), Is.False);
		Assert.That(authorization, Is.Null);
		Assert.That(browser.Parameters["code_challenge"], Is.EqualTo(GetCodeChallenge(tokenForm["code_verifier"])));
	}

	[Test]
	public void Login_UserDeclines_ThrowsAccessDenied()
	{
		// Arrange
		using var handler = CreateDiscoveryHandler();
		var browser = new AuthorizingBrowser { Error = "access_denied" };
		var sut = CreateSut(handler, browser);

		// Act
		// Assert
		Assert.That(async () => await sut.Login(CancellationToken.None),
			Throws.InstanceOf<OAuthAuthorizationException>()
				.With.Property(nameof(OAuthAuthorizationException.Error)).EqualTo(OAuthAuthorizationException.AccessDeniedError));
	}

	[Test]
	public async Task Refresh_ValidRefreshToken_ReturnsRotatedTokens()
	{
		// Arrange
		using var handler = CreateDiscoveryHandler();
		handler.Expect(HttpMethod.Post, TokenEndpoint)
			.WithFormData(new Dictionary<string, string>
			{
				["grant_type"] = "refresh_token",
				["refresh_token"] = "old-refresh",
				["client_id"] = ClientId,
			})
			.Respond(_ => TokenResponse("new-access", "new-refresh", idToken: null));
		var sut = CreateSut(handler, new AuthorizingBrowser());

		// Act
		var token = await sut.Refresh("old-refresh", CancellationToken.None);

		// Assert
		Assert.That(token, Is.Not.Null);
		Assert.That(token!.AccessToken, Is.EqualTo("new-access"));
		Assert.That(token.RefreshToken, Is.EqualTo("new-refresh"));
	}

	[Test]
	public async Task Refresh_AuthorizationRevoked_ReturnsNull()
	{
		// Arrange
		using var handler = CreateDiscoveryHandler();
		handler.Expect(HttpMethod.Post, TokenEndpoint)
			.Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"invalid_grant"}""");
		var sut = CreateSut(handler, new AuthorizingBrowser());

		// Act
		var token = await sut.Refresh("old-refresh", CancellationToken.None);

		// Assert
		Assert.That(token, Is.Null);
	}

	[Test]
	public void Refresh_OtherError_ThrowsOAuthAuthorizationException()
	{
		// Arrange
		using var handler = CreateDiscoveryHandler();
		handler.Expect(HttpMethod.Post, TokenEndpoint)
			.Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"invalid_client"}""");
		var sut = CreateSut(handler, new AuthorizingBrowser());

		// Act
		// Assert
		Assert.That(async () => await sut.Refresh("old-refresh", CancellationToken.None),
			Throws.InstanceOf<OAuthAuthorizationException>()
				.With.Property(nameof(OAuthAuthorizationException.Error)).EqualTo("invalid_client"));
	}

	[Test]
	public async Task Revoke_DiscordAcceptsBothTokens_RevokesRefreshAndAccessTokens()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var refresh = handler.Expect(HttpMethod.Post, RevocationEndpoint)
			.WithFormData(new Dictionary<string, string>
			{
				["token"] = "refresh",
				["token_type_hint"] = "refresh_token",
				["client_id"] = ClientId,
			})
			.Respond(HttpStatusCode.OK);
		var access = handler.Expect(HttpMethod.Post, RevocationEndpoint)
			.WithFormData(new Dictionary<string, string>
			{
				["token"] = "access",
				["token_type_hint"] = "access_token",
				["client_id"] = ClientId,
			})
			.Respond(HttpStatusCode.OK);
		var sut = CreateSut(handler, new AuthorizingBrowser());

		// Act
		var revoked = await sut.Revoke(new OAuthToken("access", "refresh", DateTimeOffset.UnixEpoch), CancellationToken.None);

		// Assert
		Assert.That(revoked, Is.True);
		Assert.That(handler.GetMatchCount(refresh), Is.EqualTo(1));
		Assert.That(handler.GetMatchCount(access), Is.EqualTo(1));
	}

	[Test]
	public async Task Revoke_DiscordFails_ReturnsFalse()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.When(HttpMethod.Post, RevocationEndpoint).Respond(HttpStatusCode.ServiceUnavailable);
		var sut = CreateSut(handler, new AuthorizingBrowser());

		// Act
		var revoked = await sut.Revoke(new OAuthToken("access", "refresh", DateTimeOffset.UnixEpoch), CancellationToken.None);

		// Assert
		Assert.That(revoked, Is.False);
	}

	[Test]
	public async Task Revoke_RefreshTokenRequestTimesOut_ReturnsFalseAndStillRevokesAccessToken()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		handler.Expect(HttpMethod.Post, RevocationEndpoint)
			.WithFormData("token", "refresh")
			.Throw(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
		var access = handler.Expect(HttpMethod.Post, RevocationEndpoint)
			.WithFormData("token", "access")
			.Respond(HttpStatusCode.OK);
		var sut = CreateSut(handler, new AuthorizingBrowser());

		// Act
		var revoked = await sut.Revoke(new OAuthToken("access", "refresh", DateTimeOffset.UnixEpoch), CancellationToken.None);

		// Assert
		Assert.That(revoked, Is.False);
		Assert.That(handler.GetMatchCount(access), Is.EqualTo(1));
	}

	[Test]
	public void Revoke_CallerCancels_ThrowsOperationCanceledException()
	{
		// Arrange
		using var handler = MockHttpClientFactory.StrictHandler();
		var sut = CreateSut(handler, new AuthorizingBrowser());
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		// Act & Assert
		Assert.That(() => sut.Revoke(new OAuthToken("access", "refresh", DateTimeOffset.UnixEpoch), cts.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	private static DiscordOAuthClient CreateSut(MockHttpMessageHandler handler, IBrowser browser)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<DiscordOAuthClient>>(NullLogger<DiscordOAuthClient>.Instance);
		mocker.Use(browser);
		MockHttpClientFactory.Factory(mocker, AppConstants.DiscordHttpClient, handler);
		OptionsStubs.Options(mocker, new DiscordSettings { ClientId = ClientId, GuildId = "42", RedirectPort = 53134 });
		return mocker.CreateInstance<DiscordOAuthClient>(enablePrivate: true);
	}

	/// <summary>
	/// Serves Discord's discovery document (as published at https://discord.com/.well-known/openid-configuration)
	/// and a key set, also while token requests are expected; other unexpected requests fail the test.
	/// </summary>
	private static MockHttpMessageHandler CreateDiscoveryHandler()
	{
		var handler = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
		handler.Fallback.Throw(new InvalidOperationException("Unexpected HTTP request"));
		handler.When(HttpMethod.Get, "https://discord.com/.well-known/openid-configuration")
			.Respond("application/json",
				"""
				{
					"issuer": "https://discord.com",
					"authorization_endpoint": "https://discord.com/api/oauth2/authorize",
					"token_endpoint": "https://discord.com/api/oauth2/token",
					"userinfo_endpoint": "https://discord.com/api/oauth2/userinfo",
					"jwks_uri": "https://discord.com/api/oauth2/keys",
					"response_types_supported": ["code", "token"],
					"subject_types_supported": ["public"],
					"id_token_signing_alg_values_supported": ["RS256"],
					"code_challenge_methods_supported": ["S256"],
					"authorization_response_iss_parameter_supported": true
				}
				""");
		handler.When(HttpMethod.Get, "https://discord.com/api/oauth2/keys")
			.Respond("application/json",
				"""{"keys":[{"kty":"RSA","use":"sig","kid":"key","alg":"RS256","n":"sXchDaQebHnPiGvyDOAT4saGEUetSyo9MKLOoWFsueri23bOdgWp4Dy1WlUzewbgBHod5pcM9H95GQRV3JDXboIRROSBigeC5yjU1hGzHHyXss8UDprecbAYxknTcQkhslANGRUZmdTOQ5qTRsLAt6BTYuyvVRdhS8exSZEy_c4gs_7svlJJQ4H9_NxsiIoLwAEk7-Q3UXERGYw_75IDrGA84-lA_-Ct4eTlXHBIY2EaV7t7LjJaynVJCpkv4LKjTTAumiGUIuQhrNhZLuF_RJLqHpM2kgWFLU7-VTdL1VbC2tejvcI2BlMkEpk1BzBZI0KQB0GaDWFLN-aEAw3vRw","e":"AQAB"}]}""");
		return handler;
	}

	private static HttpResponseMessage TokenResponse(string accessToken, string refreshToken, string? idToken)
	{
		var json = new JsonObject
		{
			["access_token"] = accessToken,
			["token_type"] = "Bearer",
			["expires_in"] = 604800,
			["refresh_token"] = refreshToken,
			["scope"] = "openid identify guilds.members.read",
		};
		if (idToken != null)
		{
			json["id_token"] = idToken;
		}

		return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(json) };
	}

	/// <summary>
	/// An ID token shaped like Discord's; the client does not check its signature.
	/// </summary>
	private static string CreateIdToken(string nonce)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var header = new JsonObject { ["alg"] = "RS256", ["kid"] = "key", ["typ"] = "JWT" };
		var payload = new JsonObject
		{
			["iss"] = "https://discord.com",
			["aud"] = ClientId,
			["sub"] = "80351110224678912",
			["nonce"] = nonce,
			["iat"] = now,
			["exp"] = now + 604800,
		};
		return $"{Base64Url(header.ToJsonString())}.{Base64Url(payload.ToJsonString())}.c2lnbmF0dXJl";
	}

	private static string GetCodeChallenge(string codeVerifier) =>
		Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

	private static string Base64Url(string value) => Base64Url(Encoding.UTF8.GetBytes(value));

	private static string Base64Url(byte[] value) =>
		Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static async Task<IReadOnlyDictionary<string, string>> ReadForm(HttpRequestMessage request)
	{
		var query = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync());
		return query.AllKeys.ToDictionary(key => key!, key => query[key]!);
	}

	/// <summary>
	/// Browser where the user authorizes the launcher (or declines it when <see cref="Error"/> is set): Discord
	/// redirects back with a code, the state and its issuer.
	/// </summary>
	private sealed class AuthorizingBrowser : IBrowser
	{
		public const string Code = "authorization-code";

		public string? Error { get; init; }

		public IReadOnlyDictionary<string, string>? Parameters { get; private set; }

		public string? Nonce => Parameters?.GetValueOrDefault("nonce");

		public Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
		{
			var query = HttpUtility.ParseQueryString(new Uri(options.StartUrl).Query);
			Parameters = query.AllKeys.ToDictionary(key => key!, key => query[key]!);
			var response = Error is null ? $"code={Code}" : $"error={Error}";
			return Task.FromResult(new BrowserResult
			{
				ResultType = BrowserResultType.Success,
				Response = $"{options.EndUrl}?{response}&state={Uri.EscapeDataString(Parameters["state"])}&iss={Uri.EscapeDataString("https://discord.com")}",
			});
		}
	}
}
