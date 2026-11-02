namespace LauncherKit.Core.Discord;

/// <summary>
/// Discord REST API calls made with the user's OAuth access token.
/// </summary>
internal interface IDiscordApiClient
{
	/// <exception cref="HttpRequestException">
	/// The request failed; <see cref="HttpRequestException.StatusCode"/> is
	/// <see cref="System.Net.HttpStatusCode.Unauthorized"/> when the token is no longer valid.
	/// </exception>
	Task<DiscordUser> GetCurrentUser(string accessToken, CancellationToken cancellationToken);

	/// <param name="userId">The current user's ID, which the server avatar's URI contains.</param>
	/// <returns><see langword="null"/> when the user is not a member of the server.</returns>
	/// <exception cref="HttpRequestException">The request failed.</exception>
	Task<DiscordGuildMember?> GetCurrentUserGuildMember(string accessToken, ulong guildId, ulong userId,
		CancellationToken cancellationToken);
}
