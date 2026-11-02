namespace LauncherKit.Core.Discord;

/// <param name="GuildMember">
/// Membership in the configured server (<c>Discord:GuildId</c>), <see langword="null"/> when the user is not a member.
/// </param>
public sealed record DiscordAccount(DiscordUser User, DiscordGuildMember? GuildMember)
{
	/// <summary>The name shown in the configured server: the server nickname, or the global display name.</summary>
	public string DisplayName => GuildMember?.Nickname ?? User.DisplayName;

	/// <summary>The avatar shown in the configured server: the server avatar, or the user's avatar.</summary>
	public Uri AvatarUri => GuildMember?.AvatarUri ?? User.AvatarUri;

	/// <summary>Whether the user is a member of the configured server with any of <paramref name="roleIds"/>.</summary>
	public bool HasAnyRole(IEnumerable<ulong> roleIds) =>
		GuildMember is { } member && roleIds.Any(member.RoleIds.Contains);
}
