namespace LauncherKit.Core.Discord;

/// <summary>
/// <c>GET /users/@me/guilds/{guild.id}/member</c>; role IDs are JSON strings.
/// </summary>
internal sealed record DiscordGuildMemberResponse(string? Nick, string? Avatar, IReadOnlyList<ulong> Roles);
