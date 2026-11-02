namespace LauncherKit.Core.Discord;

/// <summary>
/// <c>GET /users/@me</c>; snowflake IDs are JSON strings.
/// </summary>
internal sealed record DiscordUserResponse(ulong Id, string Username, string? GlobalName, string? Avatar);
