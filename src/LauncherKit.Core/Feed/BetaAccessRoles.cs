namespace LauncherKit.Core.Feed;

/// <summary>
/// The feed's roles on the Discord server (<c>Discord:GuildId</c>) that unlock beta versions hidden behind beta codes.
/// </summary>
/// <param name="SponsorRoleIds">
/// <see cref="FeedData.SponsorRoleIds"/>: unlock the versions marked <see cref="Modpacks.ModpackFeedItem.IsAvailableForSponsors"/>.
/// </param>
/// <param name="DeveloperRoleIds"><see cref="FeedData.DeveloperRoleIds"/>: unlock every beta version.</param>
public sealed record BetaAccessRoles(IReadOnlyList<ulong> SponsorRoleIds, IReadOnlyList<ulong> DeveloperRoleIds)
{
	public static BetaAccessRoles None { get; } = new([], []);
}
