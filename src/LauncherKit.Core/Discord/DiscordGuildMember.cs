namespace LauncherKit.Core.Discord;

/// <param name="Nickname">The user's nickname in the server, if set.</param>
/// <param name="AvatarUri">The user's avatar in the server, if set.</param>
/// <param name="RoleIds">IDs of the member's roles; role IDs never change, so they can be compared with known IDs.</param>
public sealed record DiscordGuildMember(string? Nickname, Uri? AvatarUri, IReadOnlyList<ulong> RoleIds);
