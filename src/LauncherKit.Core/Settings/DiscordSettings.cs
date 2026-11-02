namespace LauncherKit.Core.Settings;

public sealed class DiscordSettings
{
	/// <summary>
	/// Client ID of the Discord application. The application must be a public client (PKCE without a client
	/// secret) with the redirect <c>http://127.0.0.1:{RedirectPort}/discord/callback</c> registered.
	/// </summary>
	public required string ClientId { get; set; }

	/// <summary>
	/// ID of the Discord server whose membership and roles of the linked account are read.
	/// </summary>
	public required string GuildId { get; set; }

	/// <summary>
	/// Loopback port the launcher listens on for the OAuth redirect.
	/// </summary>
	public required int RedirectPort { get; set; }
}
