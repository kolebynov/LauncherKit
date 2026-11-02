namespace LauncherKit.Core.Settings;

public sealed class UrlSettings
{
	public Uri MainUrl { get; set; } = null!;

	public Uri ProxyUrl { get; set; } = null!;

	public string? BearerToken { get; set; }
}
