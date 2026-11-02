using NuGet.Versioning;
using LauncherKit.Core.Feed.Install;

namespace LauncherKit.Core.Feed.Modpacks;

public sealed class ModpackPatchFeedItem
{
	public required NuGetVersion Version { get; init; }

	/// <summary>The patch archive, extracted whole into the Skyrim directory before the <see cref="InstallSteps"/> run.</summary>
	public required DownloadInfo DownloadInfo { get; init; }

	public bool RequireNewGame { get; init; }

	/// <summary>
	/// Optional steps run in order after the patch archive is extracted; <see langword="null"/> when missing. A patch is
	/// never rolled back, so <see cref="InstallStep.CanRollback"/> has no effect.
	/// </summary>
	public IReadOnlyList<InstallStep>? InstallSteps { get; init; }
}
