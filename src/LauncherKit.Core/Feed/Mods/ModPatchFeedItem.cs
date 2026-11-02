using NuGet.Versioning;
using LauncherKit.Core.Feed.Install;

namespace LauncherKit.Core.Feed.Mods;

/// <summary>
/// A patch of a mod version (<see cref="ModFeedItem.Patches"/>): it raises the installed mod to <see cref="Version"/>
/// without reinstalling it.
/// </summary>
public sealed class ModPatchFeedItem
{
	public required NuGetVersion Version { get; init; }

	/// <summary>The patch archive, extracted whole into the mod directory before the <see cref="InstallSteps"/> run.</summary>
	public required DownloadInfo DownloadInfo { get; init; }

	/// <summary>
	/// Optional steps run in order after the patch archive is extracted; <see langword="null"/> when missing. Uninstalling
	/// or updating the mod rolls back the <see cref="InstallStep.CanRollback"/> steps of every applied patch, newest patch
	/// first, before the steps of its mod version.
	/// </summary>
	public IReadOnlyList<InstallStep>? InstallSteps { get; init; }
}
