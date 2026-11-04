using System.Text.Json.Serialization;
using NuGet.Versioning;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Core.Feed;

public sealed class FeedData
{
	public required string DefaultLanguage { get; init; }

	public IReadOnlyList<InstallComponent> InstallComponents { get; init; } = [];

	public IReadOnlyList<Prerequisite> Prerequisites { get; init; } = [];

	public required DownloadInfo SkyrimDownloadInfo { get; init; }

	public required DownloadInfo SevenZipDownloadInfo { get; init; }

	public required IReadOnlyList<ModpackFeedItem> Modpacks { get; init; } = [];

	public required IReadOnlyList<ModpackChangelogFeedItem> ModpackChangelogs { get; init; } = [];

	public required IReadOnlyList<LauncherFeedItem> Launchers { get; init; } = [];

	public IReadOnlyList<GraphPackFeedData>? GraphPacks { get; init; }

	public IReadOnlyList<ModFeedData>? Mods { get; init; }

	public IReadOnlyDictionary<string, VersionRange>? BetaVersions { get; init; }

	/// <summary>
	/// IDs of the roles on the Discord server (<c>Discord:GuildId</c>) whose members are sponsors: a linked
	/// account with any of them gets the beta versions marked <see cref="ModpackFeedItem.IsAvailableForSponsors"/>.
	/// Snowflakes are written as JSON strings.
	/// </summary>
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public IReadOnlyList<ulong>? SponsorRoleIds { get; init; }

	/// <summary>
	/// IDs of the roles of modpack developers on the Discord server (<c>Discord:GuildId</c>): a linked account with
	/// any of them gets every prerelease, whether or not a beta code covers it or it is
	/// <see cref="ModpackFeedItem.IsAvailableForSponsors"/>. Snowflakes are written as JSON strings.
	/// </summary>
	[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
	public IReadOnlyList<ulong>? DeveloperRoleIds { get; init; }
}