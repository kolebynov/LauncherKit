using System.Text.Json.Serialization;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Feed;

/// <summary>
/// Editable counterpart of the launcher's <c>FeedData</c>. Versions and version ranges are kept as the original text
/// and checked by <see cref="FeedRules"/>, so unchanged values are written back byte-for-byte.
/// </summary>
public sealed class FeedModel : FeedEntity
{
	public string DefaultLanguage { get; set; } = string.Empty;

	public List<InstallComponentModel> InstallComponents { get; init => field = value ?? []; } = [];

	public List<PrerequisiteModel> Prerequisites { get; init => field = value ?? []; } = [];

	public DownloadInfoModel SkyrimDownloadInfo { get; set; } = new();

	public DownloadInfoModel SevenZipDownloadInfo { get; set; } = new();

	public List<ModpackModel> Modpacks { get; init => field = value ?? []; } = [];

	public List<ModpackChangelogModel> ModpackChangelogs { get; init => field = value ?? []; } = [];

	public List<LauncherModel> Launchers { get; init => field = value ?? []; } = [];

	[OmitWhenEmpty]
	public List<GraphPackModel> GraphPacks { get; init => field = value ?? []; } = [];

	[OmitWhenEmpty]
	public List<ModModel> Mods { get; init => field = value ?? []; } = [];

	[OmitWhenEmpty]
	[JsonConverter(typeof(BetaVersionsJsonConverter))]
	public List<BetaVersionModel> BetaVersions { get; init => field = value ?? []; } = [];

	/// <summary>
	/// IDs of the sponsor roles on the modpack Discord server; their members get the modpack beta versions marked
	/// <see cref="ModpackModel.IsAvailableForSponsors"/>. Written as JSON strings, like Discord's snowflakes.
	/// </summary>
	[OmitWhenEmpty]
	public List<string> SponsorRoleIds { get; init => field = value ?? []; } = [];

	/// <summary>
	/// IDs of the modpack developer roles on the modpack Discord server; their members get every modpack beta version hidden
	/// behind a beta code, whether or not it is <see cref="ModpackModel.IsAvailableForSponsors"/>. Written as JSON strings.
	/// </summary>
	[OmitWhenEmpty]
	public List<string> DeveloperRoleIds { get; init => field = value ?? []; } = [];
}

public sealed class DownloadInfoModel : FeedEntity
{
	public string S3Path { get; set; } = string.Empty;

	public string? Hash
	{
		get;
		set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}

public sealed class LauncherModel : FeedEntity
{
	public string Version { get; set; } = string.Empty;

	public DownloadInfoModel DownloadInfo { get; set; } = new();

	/// <summary>Null means this launcher has no changelog.</summary>
	public IReadOnlyDictionary<string, string>? ChangelogPath { get; set; }

	public bool SupportsNewModpackVersions { get; set; }
}

/// <summary>One entry of the feed's <c>BetaVersions</c> map: hashed beta code to the modpack versions it unlocks.</summary>
public sealed class BetaVersionModel
{
	public string Hash { get; set; } = string.Empty;

	public string VersionRange { get; set; } = string.Empty;
}
