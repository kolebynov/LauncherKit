using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Feed;

public enum InstallComponentKind
{
	/// <summary>One of <see cref="InstallComponentModel.Options"/>, or none.</summary>
	ModChoice,

	/// <summary>Whether to install <see cref="InstallComponentModel.ModName"/>.</summary>
	Mod,
}

public sealed class InstallComponentModel : FeedEntity
{
	public string Id { get; set; } = string.Empty;
	public InstallComponentKind Kind { get; set; }
	public string? ModName { get; set; }

	/// <summary>The mods a <see cref="InstallComponentKind.ModChoice"/> component offers, in display order.</summary>
	[OmitWhenEmpty]
	public List<ModChoiceOptionModel> Options { get; init => field = value ?? []; } = [];

	public Dictionary<string, string> Title { get; init => field = value ?? []; } = [];
	public Dictionary<string, string> Description { get; init => field = value ?? []; } = [];
	public string SupportedModpackVersion { get; set; } = string.Empty;
	public int Order { get; set; }
}

/// <summary>A mod offered by a <see cref="InstallComponentKind.ModChoice"/> component.</summary>
public sealed class ModChoiceOptionModel : FeedEntity
{
	public string ModName { get; set; } = string.Empty;

	/// <summary>Selected by default when installing modpack; at most one option of a component is.</summary>
	public bool IsDefault { get; set; }
}

public sealed class PrerequisiteModel : FeedEntity
{
	public string Id { get; set; } = string.Empty;
	public DownloadInfoModel DownloadInfo { get; set; } = new();
	public string Arguments { get; set; } = string.Empty;
}
