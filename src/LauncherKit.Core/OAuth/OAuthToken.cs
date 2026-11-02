namespace LauncherKit.Core.OAuth;

/// <summary>
/// Tokens of an OAuth authorization; <paramref name="ExpiresAt"/> is when <paramref name="AccessToken"/> expires.
/// </summary>
internal sealed record OAuthToken(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
