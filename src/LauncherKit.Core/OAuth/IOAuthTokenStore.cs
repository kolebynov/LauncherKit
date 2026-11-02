using System.Security.Cryptography;

namespace LauncherKit.Core.OAuth;

/// <summary>
/// Persists the tokens of each OAuth provider in the global key-value storage, encrypted for the current Windows
/// user.
/// </summary>
internal interface IOAuthTokenStore
{
	/// <exception cref="CryptographicException">
	/// The stored tokens cannot be decrypted, e.g. <c>launcher.db</c> was copied from another Windows user.
	/// </exception>
	OAuthToken? Get(string provider);

	void Set(string provider, OAuthToken token);

	void Remove(string provider);
}
