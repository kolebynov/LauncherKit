using NuGet.Versioning;
using LauncherKit.Core.Feed.Install;

namespace LauncherKit.Core.Feed.Mods;

public class ModFeedItem
{
	public required NuGetVersion Version { get; init; }

	/// <summary>
	/// The mod's archive, downloaded and installed as the mod before the <see cref="InstallSteps"/> run: a FOMOD archive
	/// is configured by the user, any other archive is extracted whole into the mod directory.
	/// </summary>
	public required DownloadInfo ArchiveDownloadInfo { get; init; }

	/// <summary>Where the mod goes in the mod list; <see langword="null"/> for Mod Organizer's default placement.</summary>
	public ModPlacement? Placement { get; init; }

	/// <summary>Optional steps run after the mod archive is installed; <see langword="null"/> when missing.</summary>
	public IReadOnlyList<InstallStep>? InstallSteps { get; init; }

	/// <summary>
	/// Patches of the version, applied in version order after it is installed; <see langword="null"/> when missing.
	/// Each patch version is above <see cref="Version"/>, and the version and its patches are unique within the mod.
	/// </summary>
	public IReadOnlyList<ModPatchFeedItem>? Patches { get; init; }

	public required VersionRange SupportedModpackVersion { get; init; }

	/// <summary>The version reached once every patch is applied.</summary>
	public NuGetVersion GetLatestVersion() => Patches?.Max(x => x.Version) is { } patchVersion && patchVersion > Version
		? patchVersion
		: Version;

	/// <summary>The patches up to <paramref name="installedVersion"/>, in the order they were applied.</summary>
	public IEnumerable<ModPatchFeedItem> GetAppliedPatches(NuGetVersion installedVersion) =>
		(Patches ?? []).Where(x => x.Version <= installedVersion).OrderBy(x => x.Version);

	/// <summary>Whether <paramref name="version"/> is this version or one of its patches.</summary>
	public bool Includes(NuGetVersion version) => Version == version || (Patches?.Any(x => x.Version == version) ?? false);

	/// <summary>The patches above <paramref name="version"/>, in the order they are applied.</summary>
	public IEnumerable<ModPatchFeedItem> GetPatchesAfter(NuGetVersion version) =>
		(Patches ?? []).Where(x => x.Version > version).OrderBy(x => x.Version);

	/// <summary>
	/// Whether removing the mod after rolling back the version's <see cref="InstallStep.CanRollback"/> steps fully
	/// removes it, so the launcher offers to uninstall it. Optional in the feed, <see langword="false"/> when missing.
	/// </summary>
	public bool SupportsUninstall { get; init; }

	/// <summary>
	/// Every file installing the version may download: <see cref="ArchiveDownloadInfo"/>, then the files of the
	/// <see cref="InstallSteps"/>' actions, including steps whose conditions may skip them, then each patch's archive and
	/// the files of its steps' actions.
	/// </summary>
	public IEnumerable<DownloadInfo> GetDownloads() =>
		GetStepDownloads(InstallSteps)
			.Prepend(ArchiveDownloadInfo)
			.Concat(GetPatchesAfter(Version).SelectMany(patch => GetStepDownloads(patch.InstallSteps).Prepend(patch.DownloadInfo)));

	private static IEnumerable<DownloadInfo> GetStepDownloads(IReadOnlyList<InstallStep>? steps) =>
		(steps ?? [])
		.SelectMany(step => step.Actions)
		.Select(action => action switch
		{
			InstallAction.InstallMod installMod => installMod.DownloadInfo,
			InstallAction.ExtractArchive extractArchive => extractArchive.DownloadInfo,
			InstallAction.MergePlugins mergePlugins => mergePlugins.DownloadInfo,
			InstallAction.SelectRequiredMod or InstallAction.SelectChoices or InstallAction.ActivateMods
				or InstallAction.ActivatePlugins
				or InstallAction.RemoveFileSystemItem or InstallAction.ChangeProfile => null,
		})
		.OfType<DownloadInfo>();
}