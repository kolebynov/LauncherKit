namespace LauncherKit.FeedEditor.Feed;

public enum InstallComponentKind
{
	GraphPack,
	Mod,
}

public sealed class InstallComponentModel : FeedEntity
{
	public string Id { get; set; } = string.Empty;
	public InstallComponentKind Kind { get; set; }
	public string? ModName { get; set; }
	public Dictionary<string, string> Title { get; init => field = value ?? []; } = [];
	public Dictionary<string, string> Description { get; init => field = value ?? []; } = [];
	public string SupportedModpackVersion { get; set; } = string.Empty;
	public int Order { get; set; }
}

public sealed class PrerequisiteModel : FeedEntity
{
	public string Id { get; set; } = string.Empty;
	public DownloadInfoModel DownloadInfo { get; set; } = new();
	public string Arguments { get; set; } = string.Empty;
}
