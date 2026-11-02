namespace LauncherKit.Core.Discord;

/// <param name="DisplayName">The global display name, or <paramref name="UserName"/> when the user has none.</param>
/// <param name="AvatarUri">The user's avatar, or Discord's default avatar when the user has none.</param>
public sealed record DiscordUser(ulong Id, string UserName, string DisplayName, Uri AvatarUri);
