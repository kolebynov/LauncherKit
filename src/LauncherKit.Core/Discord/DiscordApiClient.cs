using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace LauncherKit.Core.Discord;

internal sealed class DiscordApiClient : IDiscordApiClient
{
	/// <summary>
	/// Base address of the <see cref="AppConstants.DiscordHttpClient"/> client.
	/// </summary>
	public static readonly Uri BaseUri = new("https://discord.com/api/v10/");

	private const int DefaultAvatarCount = 6;
	private const int SnowflakeTimestampShift = 22;

	private readonly IHttpClientFactory _httpClientFactory;

	public DiscordApiClient(IHttpClientFactory httpClientFactory)
	{
		_httpClientFactory = httpClientFactory;
	}

	public async Task<DiscordUser> GetCurrentUser(string accessToken, CancellationToken cancellationToken)
	{
		using var response = await Get("users/@me", accessToken, cancellationToken);
		response.EnsureSuccessStatusCode();
		var user = await response.Content.ReadFromJsonAsync(DiscordJsonContext.Default.DiscordUserResponse,
			cancellationToken) ?? throw new InvalidOperationException("Discord returned a null user");
		return new DiscordUser(user.Id, user.Username, user.GlobalName ?? user.Username, GetAvatarUri(user));
	}

	public async Task<DiscordGuildMember?> GetCurrentUserGuildMember(string accessToken, ulong guildId, ulong userId,
		CancellationToken cancellationToken)
	{
		using var response = await Get(
			string.Create(CultureInfo.InvariantCulture, $"users/@me/guilds/{guildId}/member"),
			accessToken,
			cancellationToken);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();
		var member = await response.Content.ReadFromJsonAsync(DiscordJsonContext.Default.DiscordGuildMemberResponse,
			cancellationToken) ?? throw new InvalidOperationException("Discord returned a null guild member");
		return new DiscordGuildMember(member.Nick, GetGuildAvatarUri(member, guildId, userId), member.Roles);
	}

	private async Task<HttpResponseMessage> Get(string path, string accessToken, CancellationToken cancellationToken)
	{
		using var httpClient = _httpClientFactory.CreateClient(AppConstants.DiscordHttpClient);
		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		return await httpClient.SendAsync(request, cancellationToken);
	}

	/// <summary>
	/// See https://discord.com/developers/docs/reference#image-formatting.
	/// </summary>
	private static Uri GetAvatarUri(DiscordUserResponse user) =>
		new(user.Avatar is { } avatar
			? string.Create(CultureInfo.InvariantCulture, $"https://cdn.discordapp.com/avatars/{user.Id}/{avatar}.png?size=128")
			: string.Create(CultureInfo.InvariantCulture,
				$"https://cdn.discordapp.com/embed/avatars/{(user.Id >> SnowflakeTimestampShift) % DefaultAvatarCount}.png"));

	private static Uri? GetGuildAvatarUri(DiscordGuildMemberResponse member, ulong guildId, ulong userId) =>
		member.Avatar is { } avatar
			? new Uri(string.Create(CultureInfo.InvariantCulture,
				$"https://cdn.discordapp.com/guilds/{guildId}/users/{userId}/avatars/{avatar}.png?size=128"))
			: null;
}
