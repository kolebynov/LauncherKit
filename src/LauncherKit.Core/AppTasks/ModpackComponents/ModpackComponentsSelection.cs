using LauncherKit.Core.Feed;

namespace LauncherKit.Core.AppTasks.ModpackComponents;

public sealed record GraphPackComponent(
	string Name, string Recommendation, AppTask? InstallTask, IReadOnlyList<DownloadInfo> Downloads, bool IsDefault = false);

/// <summary>A declared component and its available installation options.</summary>
public sealed record ModpackComponentOffer(InstallComponent Definition, IReadOnlyList<GraphPackComponent> Options);

public sealed record ModpackComponentsOffer(
	IReadOnlyList<ModpackComponentOffer> Components, ModpackComponentsSelection Selection);

/// <summary>Choices shared by the selection task and the ordered component installation tasks.</summary>
public sealed class ModpackComponentsSelection
{
	public bool IsApplied { get; set; }
	public Dictionary<string, AppTask?> InstallTasks { get; } = new(StringComparer.Ordinal);
}
