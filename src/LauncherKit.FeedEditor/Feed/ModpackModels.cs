using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Feed;

public sealed class ModpackModel : FeedEntity
{
	public string InitialVersion { get; set; } = string.Empty;

	public string Version { get; set; } = string.Empty;

	public DownloadInfoModel DownloadInfo { get; set; } = new();

	[OmitWhenEmpty]
	public List<ModpackPatchModel> Patches { get; init => field = value ?? []; } = [];

	/// <summary>
	/// A beta version hidden behind beta codes that sponsors (<see cref="FeedModel.SponsorRoleIds"/>) get without a
	/// code.
	/// </summary>
	public bool IsAvailableForSponsors { get; set; }
}

public sealed class ModpackPatchModel : FeedEntity
{
	public string Version { get; set; } = string.Empty;

	/// <summary>The patch archive, extracted whole into the Skyrim directory before the <see cref="InstallSteps"/>.</summary>
	public DownloadInfoModel DownloadInfo { get; set; } = new();

	public bool RequireNewGame { get; set; }

	/// <summary>Steps run in order after the patch archive is extracted; a patch is never rolled back.</summary>
	[OmitWhenEmpty]
	public List<ModInstallStepModel> InstallSteps { get; init => field = value ?? []; } = [];
}

public sealed class ModpackChangelogModel : FeedEntity
{
	public string Version { get; set; } = string.Empty;

	public Dictionary<string, string> Path { get; init => field = value ?? []; } = [];
}
