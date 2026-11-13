using LauncherKit.Core.Feed;

namespace LauncherKit.Core.AppTasks.ModpackComponents;

/// <summary>
/// A mod offered by a component; without <paramref name="InstallTask"/> the mod is not updated for the modpack version
/// and the option is unavailable.
/// </summary>
public sealed record ModComponentOption(
	string Name, string? OptionDescription, AppTask? InstallTask, IReadOnlyList<DownloadInfo> Downloads, bool IsDefault = false);

/// <summary>A declared component and its available installation options.</summary>
public sealed record ModpackComponentOffer(InstallComponent Definition, IReadOnlyList<ModComponentOption> Options);

public sealed record ModpackComponentsOffer(
	IReadOnlyList<ModpackComponentOffer> Components, ModpackComponentsSelection Selection);

/// <summary>Choices shared by the selection task and the ordered component installation tasks.</summary>
public sealed class ModpackComponentsSelection
{
	public bool IsApplied { get; set; }
	public Dictionary<string, AppTask?> InstallTasks { get; } = new(StringComparer.Ordinal);
}
