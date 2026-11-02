using LauncherKit.Core.Exceptions;
using LauncherKit.Core.OAuth;

namespace LauncherKit.Core.Discord;

/// <summary>
/// Discord OAuth2 authorization code flow with PKCE for a public client.
/// </summary>
internal interface IDiscordOAuthClient
{
	/// <summary>
	/// Opens the Discord authorization page in the browser and exchanges the code it redirects back with.
	/// </summary>
	/// <exception cref="OAuthAuthorizationException">Discord or the browser did not authorize the launcher.</exception>
	Task<OAuthToken> Login(CancellationToken cancellationToken);

	/// <returns><see langword="null"/> when the authorization was revoked or the refresh token expired.</returns>
	/// <exception cref="OAuthAuthorizationException">The refresh failed for another reason.</exception>
	Task<OAuthToken?> Refresh(string refreshToken, CancellationToken cancellationToken);

	/// <returns>Whether Discord confirmed the revocation.</returns>
	Task<bool> Revoke(OAuthToken token, CancellationToken cancellationToken);
}
