namespace LauncherKit.FeedEditor.Feed;

/// <summary>Only Launchers is interpreted. All other V11 root fields stay opaque extension data.</summary>
public sealed class LegacyFeedModel : FeedEntity
{
	public List<LegacyLauncherModel> Launchers { get; init => field = value ?? []; } = [];
}

public sealed class LegacyLauncherModel : FeedEntity
{
	public string Version { get; set; } = string.Empty;
	public DownloadInfoModel DownloadInfo { get; set; } = new();
	public string? ChangelogPath
	{
		get;
		set => field = string.IsNullOrWhiteSpace(value) ? null : value;
	}
	public bool SupportsNewStbVersions { get; set; }
}
