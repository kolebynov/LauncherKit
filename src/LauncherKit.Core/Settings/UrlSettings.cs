namespace LauncherKit.Core.Settings;

public sealed class UrlSettings
{
	public Uri MainUrl { get; set; } = null!;

	/// <summary>
	/// The fallback server, tried when <see cref="MainUrl"/> fails because of the server. Optional: without it every
	/// request goes to <see cref="MainUrl"/> only.
	/// </summary>
	public Uri? ProxyUrl { get; set; }

	public string? BearerToken { get; set; }
}
